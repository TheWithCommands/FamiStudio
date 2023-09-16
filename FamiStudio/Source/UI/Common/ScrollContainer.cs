using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FamiStudio
{
    public class ScrollContainer : Container
    {
        const int DefaultScrollBarThickness1 = 10;
        const int DefaultScrollBarThickness2 = 16;
        const int DefaultScrollBarMargin     = 8;
        const int DefaultScrollStepSize      = 60;

        private int virtualSizeY;
        private int scrollbarWidth  = DpiScaling.ScaleForWindow(Settings.ScrollBars >= 2 ? DefaultScrollBarThickness2 : DefaultScrollBarThickness1);
        private int scrollbarMargin = DpiScaling.ScaleForWindow(DefaultScrollBarMargin);
        private int scrollStepSize  = DpiScaling.ScaleForWindow(DefaultScrollStepSize);

        private bool scrolling = false;
        private int captureScrollY;
        private int captureMouseY;

        public int VirtualSizeY { get => virtualSizeY; set => SetAndMarkDirty(ref virtualSizeY, value); }
        public int ScrollBarSpacing => scrollbarWidth + scrollbarMargin;

        public ScrollContainer()
        {
            canFocus = false;
        }

        public override void ContainerMouseWheelNotify(Control control, MouseEventArgs e)
        {
            if (!e.Handled)
            {
                OnMouseWheel(e);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var deltaY = e.ScrollY > 0 ? scrollStepSize : -scrollStepSize;
            SetScroll(scrollY - deltaY);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // MATTT : If user clicks outside of the thumb, scroll up/down by a fixed amount.
            if (e.X >= width - scrollbarWidth)
            {
                captureMouseY = e.Y;
                captureScrollY = scrollY;
                UpdateScroll(e.Y);
                scrolling = true;
                Capture = true;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (scrolling)
            {
                scrolling = false;
                Capture = false;
                UpdateScroll(e.Y);
                MarkDirty();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (scrolling)
            {
                UpdateScroll(e.Y);
            }
        }

        private void UpdateScroll(int y)
        {
            ClearDialogFocus(); // This will close drop-downs, etc.
            SetScroll(captureScrollY + ((y - captureMouseY) * virtualSizeY / height));
        }

        private void SetScroll(int newScrollY)
        {
            newScrollY = Utils.Clamp(newScrollY, 0, virtualSizeY - height);
            SetAndMarkDirty(ref scrollY, newScrollY);
        }

        private void GetScrollBarParams(out int posY, out int sizeY)
        {
            sizeY = (int)Math.Round(height * (height  / (float)virtualSizeY));
            posY  = (int)Math.Round(height * (scrollY / (float)virtualSizeY));
        }

        protected override void OnRender(Graphics g)
        {
            var c = g.DefaultCommandList;

            c.FillRectangle(0, 0, width, height, Theme.DarkGreyColor4);

            base.OnRender(g);

            GetScrollBarParams(out var scrollBarPosY, out var scrollBarSizeY);

            c.PushTranslation(width - scrollbarWidth - 1, 0);
            c.FillAndDrawRectangle(0, 0, scrollbarWidth, height - 1, Theme.DarkGreyColor4, Theme.LightGreyColor1);
            c.FillAndDrawRectangle(0, scrollBarPosY - 1, Width - 1, scrollBarPosY + scrollBarSizeY, Theme.MediumGreyColor1, Theme.LightGreyColor1);
            c.PopTransform();
        }

        //public override void Render(Graphics g)
        //{
        //    g.PushClipRegion(0, 0, width, height);
        //    OnRender(g);
        //    g.PopClipRegion();
        //}
    }
}
