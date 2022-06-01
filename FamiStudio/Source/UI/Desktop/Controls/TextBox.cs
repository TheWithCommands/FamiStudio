﻿using System;
using System.Drawing;
using System.Diagnostics;

namespace FamiStudio
{
    // TODO:
    //  - Copy/paste (use GLFW clipboard for strings!)

    public class TextBox : Control
    {
        private string text;
        private int scrollX;
        private int maxScrollX;
        private int selectionStart = -1;
        private int selectionLength = 0;
        private int caretIndex = 0;
        private int widthNoMargin;
        private int mouseSelectionChar;
        private bool mouseSelecting;
        private bool caretBlink = true;
        private float caretBlinkTime;

        private Color foreColor     = Theme.LightGreyFillColor1;
        private Color disabledColor = Theme.MediumGreyFillColor1;
        private Color backColor     = Theme.DarkGreyLineColor1;

        private Brush foreBrush;
        private Brush disabledBrush;
        private Brush backBrush;
        private Brush selBrush;
        
        private int topMargin    = DpiScaling.ScaleForMainWindow(3);
        private int sideMargin   = DpiScaling.ScaleForMainWindow(4);
        private int scrollAmount = DpiScaling.ScaleForMainWindow(20);

        public Color ForeColor     { get => foreColor;     set { foreColor     = value; foreBrush     = null; MarkDirty(); } }
        public Color DisabledColor { get => disabledColor; set { disabledColor = value; disabledBrush = null; MarkDirty(); } }
        public Color BackColor     { get => backColor;     set { backColor     = value; backBrush     = null;  selBrush = null; MarkDirty(); } }

        public TextBox(string txt)
        {
            height = DpiScaling.ScaleForMainWindow(24);
            text = txt;
            //text = "Hello this is a very   ₭₭ long text bla bla bla toto titi tata tutu"; // For debugging.
        }

        public string Text
        {
            get { return text; }
            set 
            { 
                text = value;
                scrollX = 0;
                caretIndex = 0;
                selectionStart = 0;
                selectionLength = 0;
                UpdateScrollParams();
                MarkDirty(); 
            }
        }

        private void UpdateScrollParams()
        {
            maxScrollX = Math.Max(0, ThemeResources.FontMedium.MeasureString(text, false) - (width - sideMargin * 2));
            scrollX = Utils.Clamp(scrollX, 0, maxScrollX);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor.Current = enabled ? Cursors.IBeam : Cursors.Default;

            if (mouseSelecting)
            {
                var c = PixelToChar(e.X);
                var selMin = Math.Min(mouseSelectionChar, c);
                var selMax = Math.Max(mouseSelectionChar, c);

                SetAndMarkDirty(ref caretIndex, c);
                SetAndMarkDirty(ref selectionStart, selMin);
                SetAndMarkDirty(ref selectionLength, selMax - selMin);
                EnsureCaretVisible();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Left && enabled)
            {
                var c = PixelToChar(e.X);
                SetAndMarkDirty(ref caretIndex, c);
                SetAndMarkDirty(ref selectionStart, c);
                SetAndMarkDirty(ref selectionLength, 0);
                ClearSelection();
                ResetCaretBlink();

                mouseSelectionChar = c;
                mouseSelecting = true;
                Capture = true;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Left && enabled)
            {
                mouseSelecting = false;
                Capture = false;
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Left && enabled)
            { 
                var c0 = PixelToChar(e.X);
                var c1 = c0;

                // MATTT : This is buggy, clicking on a space select both left/right words.
                c0 = FindWordStart(c0, -1);
                c1 = FindWordStart(c1,  1);

                selectionStart  = c0;
                selectionLength = c1 - c0;
                caretIndex      = c1;

                MarkDirty();
            }
        }

        private int FindWordStart(int c, int dir)
        {
            if (dir > 0)
            {
                while (c < text.Length && !char.IsWhiteSpace(text[c]))
                    c++;
                while (c < text.Length && char.IsWhiteSpace(text[c]))
                    c++;
            }
            else
            {
                while (c >= 1 &&  char.IsWhiteSpace(text[c - 1]))
                    c--;
                while (c >= 1 && !char.IsWhiteSpace(text[c - 1]))
                    c--;
            }

            Debug.Assert(c >= 0 && c <= text.Length);
            return c;
        }


        // MATTT : See if GLFW (or GTK) has key repeat, if it doesnt, well need to 
        // handle it ourselves.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!enabled)
                return;

            // MATTT : Copy/paste.
            if (e.Key == Keys.Left || e.Key == Keys.Right)
            {
                var sign = e.Key == Keys.Left ? -1 : 1;
                var prevCaretIndex = caretIndex;

                if (e.Control || e.Alt)
                    caretIndex = FindWordStart(caretIndex, sign);
                else
                    caretIndex = Utils.Clamp(caretIndex + sign, 0, text.Length);

                if (e.Shift)
                {
                    var minCaret = Math.Min(prevCaretIndex, caretIndex);
                    var maxCaret = Math.Max(prevCaretIndex, caretIndex);

                    if (selectionLength == 0)
                    {
                        SetSelection(minCaret, maxCaret - minCaret);
                    }
                    else 
                    {
                        var selMin = selectionStart;
                        var selMax = selectionStart + selectionLength;

                        // This seem WAYYYY over complicated.
                        if (caretIndex < selMax && prevCaretIndex < selMax)
                            SetSelection(caretIndex, selMax - caretIndex);
                        else if (caretIndex >= selMin && prevCaretIndex > selMin)
                            SetSelection(selMin, caretIndex - selMin);
                        else if (caretIndex < selMin && prevCaretIndex > selMin)
                            SetSelection(caretIndex, selMin - caretIndex);
                        else if (caretIndex >= selMax && prevCaretIndex < selMax)
                            SetSelection(selMax, caretIndex - selMax);
                    }
                }
                else
                {
                    ClearSelection();
                }

                ResetCaretBlink();
                EnsureCaretVisible();
                MarkDirty();
            }
            else if (e.Key == Keys.A && e.Control)
            {
                SelectAll();
            }
            else if (e.Key == Keys.Backspace)
            {
                if (!DeleteSelection() && caretIndex > 0)
                {
                    caretIndex--;
                    text = RemoveStringRange(caretIndex, 1);
                    UpdateScrollParams();
                    MarkDirty();
                }
            }
            else if (e.Key == Keys.Delete)
            {
                if (!DeleteSelection() && caretIndex < text.Length)
                {
                    text = RemoveStringRange(caretIndex, 1);
                    UpdateScrollParams();
                    MarkDirty();
                }
            }
            else if ((int)e.Key >= 0 && (int)e.Key <= 255 && ThemeResources.FontMedium.GetCharInfo((char)e.Key, false) != null)
            {
                // MATTT : This is janky. Need equivalent of OnKeyPress().
                DeleteSelection();
                text = text.Insert(caretIndex, ((char)e.Key).ToString());
                caretIndex++;
                UpdateScrollParams();
                EnsureCaretVisible();
                ClearSelection();
                MarkDirty();
            }
            else if (e.Key == Keys.Escape)
            {
                ClearDialogFocus();
                e.Handled = true;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
        }

        public override void Tick(float delta)
        {
            caretBlinkTime += delta;
            SetAndMarkDirty(ref caretBlink, Utils.Frac(caretBlinkTime) < 0.5f);
        }

        private void UpdateCaretBlink()
        {
            SetAndMarkDirty(ref caretBlink, Utils.Frac(caretBlinkTime) < 0.5f);
        }

        private void ResetCaretBlink()
        {
            caretBlinkTime = 0;
            UpdateCaretBlink();
        }

        private void EnsureCaretVisible()
        {
            var px = CharToPixel(caretIndex, false);
            if (px < 0)
                SetAndMarkDirty(ref scrollX, Utils.Clamp(scrollX + px - scrollAmount, 0, maxScrollX));
            else if (px > widthNoMargin)
                SetAndMarkDirty(ref scrollX, Utils.Clamp(scrollX + px - widthNoMargin + scrollAmount, 0, maxScrollX));
        }

        private int PixelToChar(int x, bool margin = true)
        {
            return ThemeResources.FontMedium.GetNumCharactersForSize(text, x - (margin ? sideMargin : 0) + scrollX, true);
        }

        private int CharToPixel(int c, bool margin = true)
        {
            var px = (margin ? sideMargin : 0) - scrollX;
            if (c > 0)
                px += ThemeResources.FontMedium.MeasureString(text.Substring(0, c), false);
            return px;
        }

        public void SetSelection(int start, int len)
        {
            SetAndMarkDirty(ref selectionStart, start);
            SetAndMarkDirty(ref selectionLength, Math.Max(0, len));
        }

        public void ClearSelection()
        {
            SetAndMarkDirty(ref selectionStart, 0);
            SetAndMarkDirty(ref selectionLength, 0);
        }

        public void SelectAll()
        {
            selectionStart = 0;
            selectionLength = text.Length;
            caretIndex = text.Length;
            MarkDirty();
        }

        private string RemoveStringRange(int start, int len)
        {
            var left  = text.Substring(0, start);
            var right = text.Substring(start + len);

            return left + right;
        }

        private bool DeleteSelection()
        {
            if (selectionLength > 0)
            {
                text = RemoveStringRange(selectionStart, selectionLength);

                if (caretIndex >= selectionStart)
                    caretIndex -= selectionLength;

                ClearSelection();
                UpdateScrollParams();

                return true;
            }

            return false;
        }

        protected override void OnAddedToDialog()
        {
            widthNoMargin = width - sideMargin * 2;
            UpdateScrollParams();
        }

        protected override void OnRender(Graphics g)
        {
            var c = parentDialog.CommandList;

            if (foreBrush     == null) foreBrush     = g.CreateSolidBrush(foreColor);
            if (disabledBrush == null) disabledBrush = g.CreateSolidBrush(disabledColor);
            if (backBrush     == null) backBrush     = g.CreateSolidBrush(backColor);
            if (selBrush      == null) selBrush      = g.CreateSolidBrush(Theme.Darken(backColor));

            c.FillAndDrawRectangle(0, 0, width - 1, height - 1, backBrush, enabled ? foreBrush : disabledBrush);
            
            if (selectionLength > 0 && HasDialogFocus && enabled)
            {
                var sx0 = Utils.Clamp(CharToPixel(selectionStart), sideMargin, width - sideMargin);
                var sx1 = selectionLength > 0 ? Utils.Clamp(CharToPixel(selectionStart + selectionLength), sideMargin, width - sideMargin) : sx0;

                if (sx0 != sx1)
                    c.FillRectangle(sx0, topMargin, sx1, height - topMargin, selBrush);
            }

            c.DrawText(text, ThemeResources.FontMedium, sideMargin - scrollX, 0, enabled ? foreBrush : disabledBrush, TextFlags.MiddleLeft | TextFlags.Clip, 0, height, sideMargin, width - sideMargin);

            if (caretBlink && HasDialogFocus && enabled)
            {
                var cx = CharToPixel(caretIndex);
                c.DrawLine(cx, topMargin, cx, height - topMargin, foreBrush);
            }
        }
    }
}
