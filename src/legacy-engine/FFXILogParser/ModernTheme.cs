// Created for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WaywardGamers.KParser
{
    /// <summary>
    /// Applies the Sanctum visual theme without changing plugin or parser behavior.
    /// Controls added later by plugins are themed through ControlAdded events.
    /// </summary>
    internal static class ModernTheme
    {
        internal static readonly Color WindowColor = Color.FromArgb(27, 30, 35);
        internal static readonly Color PanelColor = Color.FromArgb(34, 38, 44);
        internal static readonly Color RaisedColor = Color.FromArgb(44, 49, 57);
        internal static readonly Color FieldColor = Color.FromArgb(22, 25, 29);
        internal static readonly Color BorderColor = Color.FromArgb(66, 73, 83);
        internal static readonly Color AccentColor = Color.FromArgb(202, 166, 86);
        internal static readonly Color AccentHoverColor = Color.FromArgb(224, 190, 111);
        internal static readonly Color TextColor = Color.FromArgb(235, 237, 240);
        internal static readonly Color MutedTextColor = Color.FromArgb(170, 176, 184);
        internal static readonly Color CanvasColor = Color.FromArgb(247, 245, 239);
        internal static readonly Color CanvasTextColor = Color.FromArgb(31, 34, 38);

        private static readonly Font InterfaceFont = new Font("Segoe UI", 8.75f, FontStyle.Regular);
        private static readonly HashSet<Form> ThemedForms = new HashSet<Form>();
        private static readonly HashSet<Control> WiredControls = new HashSet<Control>();
        private static readonly ToolStripRenderer Renderer = new SanctumRenderer();

        internal static void Initialize()
        {
            Application.Idle += Application_Idle;
        }

        internal static void Apply(Form form)
        {
            if ((form == null) || ThemedForms.Contains(form))
                return;

            ThemedForms.Add(form);
            form.FormClosed += Form_FormClosed;
            form.BackColor = WindowColor;
            form.ForeColor = TextColor;
            form.Font = InterfaceFont;

            if (form is ParserWindow)
            {
                form.Text = "KParser - Sanctum Edition";
                form.MinimumSize = new Size(720, 480);
            }

            ThemeControl(form);
        }

        private static void Application_Idle(object sender, EventArgs e)
        {
            for (int index = 0; index < Application.OpenForms.Count; index++)
                Apply(Application.OpenForms[index]);
        }

        private static void Form_FormClosed(object sender, FormClosedEventArgs e)
        {
            Form form = sender as Form;
            if (form != null)
                ThemedForms.Remove(form);
        }

        private static void Control_ControlAdded(object sender, ControlEventArgs e)
        {
            ThemeControl(e.Control);
        }

        private static void ThemeControl(Control control)
        {
            if (control == null)
                return;

            if (WiredControls.Add(control))
                control.ControlAdded += Control_ControlAdded;

            control.Font = InterfaceFont;
            control.ForeColor = TextColor;

            RichTextBox richTextBox = control as RichTextBox;
            if (richTextBox != null)
            {
                // Reports contain legacy RTF colors intended for a light document.
                // Keep the report canvas light while modernizing the surrounding chrome.
                richTextBox.BackColor = CanvasColor;
                richTextBox.ForeColor = CanvasTextColor;
                richTextBox.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is TextBoxBase)
            {
                control.BackColor = FieldColor;
                control.ForeColor = TextColor;
            }
            else if (control is ListBox)
            {
                control.BackColor = FieldColor;
                control.ForeColor = TextColor;
            }
            else if (control is ComboBox)
            {
                ComboBox comboBox = (ComboBox)control;
                comboBox.BackColor = FieldColor;
                comboBox.ForeColor = TextColor;
                comboBox.FlatStyle = FlatStyle.Flat;
            }
            else if (control is NumericUpDown)
            {
                control.BackColor = FieldColor;
                control.ForeColor = TextColor;
            }
            else if (control is DateTimePicker)
            {
                control.BackColor = FieldColor;
                control.ForeColor = TextColor;
            }
            else if (control is Button)
            {
                ThemeButton((Button)control);
            }
            else if (control is TabControl)
            {
                ThemeTabs((TabControl)control);
            }
            else if (control is TabPage)
            {
                control.BackColor = PanelColor;
            }
            else if (control is GroupBox)
            {
                control.BackColor = PanelColor;
                control.ForeColor = AccentColor;
            }
            else if ((control is CheckBox) || (control is RadioButton) || (control is Label))
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = TextColor;
            }
            else if (control is ToolStrip)
            {
                ThemeToolStrip((ToolStrip)control);
            }
            else if (control is PictureBox)
            {
                control.BackColor = PanelColor;
            }
            else
            {
                control.BackColor = (control is Form) ? WindowColor : PanelColor;
            }

            foreach (Control child in control.Controls)
                ThemeControl(child);
        }

        private static void ThemeButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseDownBackColor = AccentColor;
            button.UseVisualStyleBackColor = false;
            button.EnabledChanged -= Button_EnabledChanged;
            button.EnabledChanged += Button_EnabledChanged;
            UpdateButtonColors(button);
        }

        private static void Button_EnabledChanged(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button != null)
                UpdateButtonColors(button);
        }

        private static void UpdateButtonColors(Button button)
        {
            bool primary = (button.DialogResult == DialogResult.OK) ||
                           string.Equals(button.Name, "ok", StringComparison.OrdinalIgnoreCase);
            bool activePrimary = primary && button.Enabled;

            button.FlatAppearance.BorderColor = activePrimary ? AccentColor : BorderColor;
            button.FlatAppearance.MouseOverBackColor = activePrimary ? AccentHoverColor : BorderColor;
            button.BackColor = activePrimary ? AccentColor : RaisedColor;
            button.ForeColor = button.Enabled
                ? (activePrimary ? WindowColor : TextColor)
                : MutedTextColor;
        }

        private static void ThemeTabs(TabControl tabs)
        {
            tabs.BackColor = WindowColor;
            tabs.ForeColor = TextColor;
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.Padding = new Point(14, 4);
            tabs.DrawItem -= Tabs_DrawItem;
            tabs.DrawItem += Tabs_DrawItem;
        }

        private static void Tabs_DrawItem(object sender, DrawItemEventArgs e)
        {
            TabControl tabs = sender as TabControl;
            if ((tabs == null) || (e.Index < 0) || (e.Index >= tabs.TabPages.Count))
                return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Rectangle bounds = e.Bounds;
            Color background = selected ? RaisedColor : PanelColor;

            using (SolidBrush backgroundBrush = new SolidBrush(background))
                e.Graphics.FillRectangle(backgroundBrush, bounds);

            TextRenderer.DrawText(
                e.Graphics,
                tabs.TabPages[e.Index].Text,
                InterfaceFont,
                bounds,
                selected ? AccentColor : MutedTextColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);

            if (selected)
            {
                using (SolidBrush accentBrush = new SolidBrush(AccentColor))
                    e.Graphics.FillRectangle(accentBrush, bounds.Left, bounds.Bottom - 2, bounds.Width, 2);
            }
        }

        private static void ThemeToolStrip(ToolStrip strip)
        {
            strip.Renderer = Renderer;
            strip.BackColor = PanelColor;
            strip.ForeColor = TextColor;
            strip.Font = InterfaceFont;

            foreach (ToolStripItem item in strip.Items)
                ThemeToolStripItem(item);
        }

        private static void ThemeToolStripItem(ToolStripItem item)
        {
            item.BackColor = PanelColor;
            item.ForeColor = TextColor;

            ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
            if (dropDown == null)
                return;

            dropDown.DropDown.BackColor = PanelColor;
            dropDown.DropDown.ForeColor = TextColor;
            dropDown.DropDown.Renderer = Renderer;
            foreach (ToolStripItem child in dropDown.DropDownItems)
                ThemeToolStripItem(child);
        }

        private sealed class SanctumRenderer : ToolStripProfessionalRenderer
        {
            internal SanctumRenderer()
                : base(new SanctumColorTable())
            {
                RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? TextColor : MutedTextColor;
                base.OnRenderItemText(e);
            }
        }

        private sealed class SanctumColorTable : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin { get { return PanelColor; } }
            public override Color ToolStripGradientMiddle { get { return PanelColor; } }
            public override Color ToolStripGradientEnd { get { return PanelColor; } }
            public override Color MenuStripGradientBegin { get { return PanelColor; } }
            public override Color MenuStripGradientEnd { get { return PanelColor; } }
            public override Color StatusStripGradientBegin { get { return FieldColor; } }
            public override Color StatusStripGradientEnd { get { return FieldColor; } }
            public override Color ToolStripDropDownBackground { get { return PanelColor; } }
            public override Color ImageMarginGradientBegin { get { return RaisedColor; } }
            public override Color ImageMarginGradientMiddle { get { return RaisedColor; } }
            public override Color ImageMarginGradientEnd { get { return RaisedColor; } }
            public override Color MenuItemSelected { get { return RaisedColor; } }
            public override Color MenuItemSelectedGradientBegin { get { return RaisedColor; } }
            public override Color MenuItemSelectedGradientEnd { get { return RaisedColor; } }
            public override Color MenuItemPressedGradientBegin { get { return FieldColor; } }
            public override Color MenuItemPressedGradientMiddle { get { return FieldColor; } }
            public override Color MenuItemPressedGradientEnd { get { return FieldColor; } }
            public override Color MenuItemBorder { get { return AccentColor; } }
            public override Color ButtonSelectedBorder { get { return AccentColor; } }
            public override Color ButtonSelectedGradientBegin { get { return RaisedColor; } }
            public override Color ButtonSelectedGradientMiddle { get { return RaisedColor; } }
            public override Color ButtonSelectedGradientEnd { get { return RaisedColor; } }
            public override Color SeparatorDark { get { return BorderColor; } }
            public override Color SeparatorLight { get { return BorderColor; } }
        }
    }
}
