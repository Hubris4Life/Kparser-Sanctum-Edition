// Created for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace WaywardGamers.KParser.Bridge
{
    public sealed class SanctumBridgeServer : IDisposable
    {
        public const string PipeName = "KParser.Sanctum.Modern.v1";

        private const int MaxRequestBytes = 4096;
        private const int MaxResponseCharacters = 4 * 1024 * 1024;
        private static readonly SanctumBridgeServer instance = new SanctumBridgeServer();

        private readonly object stateLock = new object();
        private Thread serverThread;
        private NamedPipeServerStream activePipe;
        private Func<SanctumEngineCommand, SanctumEngineCommandResult> commandHandler;
        private bool stopRequested;

        public static SanctumBridgeServer Instance
        {
            get { return instance; }
        }

        private SanctumBridgeServer()
        {
        }

        public Func<SanctumEngineCommand, SanctumEngineCommandResult> CommandHandler
        {
            get
            {
                lock (stateLock)
                {
                    return commandHandler;
                }
            }
            set
            {
                lock (stateLock)
                {
                    commandHandler = value;
                }
            }
        }

        public void Start()
        {
            lock (stateLock)
            {
                if (serverThread != null && serverThread.IsAlive)
                    return;

                stopRequested = false;
                serverThread = new Thread(ServerLoop);
                serverThread.Name = "KParser Sanctum read-only bridge";
                serverThread.IsBackground = true;
                serverThread.Start();
            }
        }

        public void Stop()
        {
            Thread threadToJoin;

            lock (stateLock)
            {
                stopRequested = true;
                threadToJoin = serverThread;

                if (activePipe != null)
                {
                    try
                    {
                        activePipe.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            if (threadToJoin != null &&
                threadToJoin != Thread.CurrentThread &&
                threadToJoin.IsAlive)
            {
                threadToJoin.Join(1500);
            }

            lock (stateLock)
            {
                if (serverThread == threadToJoin)
                    serverThread = null;
                activePipe = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void ServerLoop()
        {
            while (IsStopRequested() == false)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = CreateCurrentUserPipe();
                    if (TrySetActivePipe(pipe) == false)
                        break;
                    pipe.WaitForConnection();

                    if (IsStopRequested())
                        break;

                    string requestText = ReadRequestLine(pipe);
                    string responseText = ProcessRequest(requestText);
                    WriteResponseLine(pipe, responseText);
                }
                catch (ObjectDisposedException)
                {
                    if (IsStopRequested() == false)
                        SafeLog("The local bridge pipe was closed unexpectedly.");
                }
                catch (IOException ex)
                {
                    if (IsStopRequested() == false)
                        SafeLog(ex, "Sanctum bridge I/O");
                }
                catch (Exception ex)
                {
                    if (IsStopRequested() == false)
                        SafeLog(ex, "Sanctum bridge");
                }
                finally
                {
                    ClearActivePipe(pipe);
                    if (pipe != null)
                        pipe.Close();
                }
            }
        }

        private static NamedPipeServerStream CreateCurrentUserPipe()
        {
            SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
            PipeSecurity security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(currentUser);
            security.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            return new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.None,
                8192,
                65536,
                security);
        }

        private static string ReadRequestLine(Stream stream)
        {
            MemoryStream buffer = new MemoryStream();
            byte[] oneByte = new byte[1];

            while (buffer.Length <= MaxRequestBytes)
            {
                int read = stream.Read(oneByte, 0, 1);
                if (read == 0 || oneByte[0] == (byte)'\n')
                    break;

                if (oneByte[0] != (byte)'\r')
                    buffer.WriteByte(oneByte[0]);
            }

            if (buffer.Length > MaxRequestBytes)
                throw new InvalidDataException("Bridge request exceeded the allowed size.");

            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        private string ProcessRequest(string requestText)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = MaxResponseCharacters;

            SanctumBridgeRequest request;
            try
            {
                request = serializer.Deserialize<SanctumBridgeRequest>(requestText);
            }
            catch (Exception)
            {
                return serializer.Serialize(CreateError("Invalid bridge request."));
            }

            if (request == null || request.Protocol != 1)
            {
                return serializer.Serialize(CreateError("Unsupported bridge request."));
            }

            ServerCompatibility.Configure(
                request.ServerProfile,
                request.PetMappingPath);
            ServerCompatibility.ConfigureLocalPlayer(request.LocalPlayerName);

            if (string.Equals(request.Type, "snapshot", StringComparison.OrdinalIgnoreCase))
                return serializer.Serialize(SanctumDamageSnapshotBuilder.Build(
                    request.Scope,
                    request.BattleId,
                    request.MobName,
                    request.Report,
                    request.CombatantScope,
                    request.DisplayMode,
                    request.GroupMode,
                    request.SearchText,
                    request.ExcludeCommonDrops));

            if (string.Equals(request.Type, "command", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(request.Command, "capturestats", StringComparison.OrdinalIgnoreCase) &&
                    ServerCompatibility.SupportsCalculatedDots == false)
                {
                    return serializer.Serialize(CreateCommandError(
                        "capturestats",
                        "Calculated DoT stat capture is unavailable for the selected server profile."));
                }
                return serializer.Serialize(ProcessCommand(
                    request.Command,
                    request.TargetPlayer));
            }

            return serializer.Serialize(CreateError("Unsupported bridge request."));
        }

        private SanctumEngineCommandResult ProcessCommand(
            string commandName,
            string targetPlayer)
        {
            string normalizedCommand = commandName == null
                ? string.Empty
                : commandName.Trim().ToLowerInvariant();

            if (IsAllowedCommand(normalizedCommand) == false)
                return CreateCommandError(normalizedCommand, "Unsupported engine command.");

            Func<SanctumEngineCommand, SanctumEngineCommandResult> handler = CommandHandler;
            if (handler == null)
                return CreateCommandError(normalizedCommand, "The engine command handler is not available.");

            try
            {
                SanctumEngineCommandResult result = handler(new SanctumEngineCommand
                {
                    Name = normalizedCommand,
                    TargetPlayer = targetPlayer == null
                        ? string.Empty
                        : targetPlayer.Trim()
                });

                return result ?? CreateCommandError(
                    normalizedCommand,
                    "The engine returned no command result.");
            }
            catch (Exception ex)
            {
                SafeLog(ex, "Sanctum engine command");
                return CreateCommandError(normalizedCommand, ex.Message);
            }
        }

        private static bool IsAllowedCommand(string commandName)
        {
            return commandName == "start" ||
                   commandName == "stop" ||
                   commandName == "reset" ||
                   commandName == "resetstopped" ||
                   commandName == "detect" ||
                   commandName == "capturestats" ||
                   commandName == "shutdown";
        }

        private static SanctumEngineCommandResult CreateCommandError(
            string commandName,
            string message)
        {
            return new SanctumEngineCommandResult
            {
                Command = commandName ?? string.Empty,
                Success = false,
                Message = message ?? "Engine command failed."
            };
        }

        private static SanctumBridgeSnapshot CreateError(string message)
        {
            SanctumBridgeSnapshot snapshot = new SanctumBridgeSnapshot();
            snapshot.Type = "error";
            snapshot.Error = message;
            return snapshot;
        }

        private static void WriteResponseLine(Stream stream, string responseText)
        {
            byte[] responseBytes = Encoding.UTF8.GetBytes(responseText + "\n");
            stream.Write(responseBytes, 0, responseBytes.Length);
            stream.Flush();
        }

        private static void SafeLog(string message)
        {
            try
            {
                Logger.Instance.Log("Sanctum bridge", message);
            }
            catch (Exception)
            {
            }
        }

        private static void SafeLog(Exception exception, string context)
        {
            try
            {
                Logger.Instance.Log(exception, context);
            }
            catch (Exception)
            {
            }
        }

        private bool IsStopRequested()
        {
            lock (stateLock)
            {
                return stopRequested;
            }
        }

        private bool TrySetActivePipe(NamedPipeServerStream pipe)
        {
            lock (stateLock)
            {
                if (stopRequested)
                    return false;

                activePipe = pipe;
                return true;
            }
        }

        private void ClearActivePipe(NamedPipeServerStream pipe)
        {
            lock (stateLock)
            {
                if (activePipe == pipe)
                    activePipe = null;
            }
        }
    }
}
