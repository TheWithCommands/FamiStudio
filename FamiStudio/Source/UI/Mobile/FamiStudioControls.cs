﻿using System;
using System.Diagnostics;
using Android.Opengl;
using Javax.Microedition.Khronos.Opengles;

namespace FamiStudio
{
    public class FamiStudioControls
    {
        private int width;
        private int height;

        private float timeDelta = 0.0f;
        private DateTime lastTime = DateTime.MinValue;

        private GLGraphics  gfx;
        private GLControl[] controls = new GLControl[5];
        private GLControl   transitionControl;
        private GLControl   activeControl;
        private GLTheme     theme;
        private float       transitionTimer;

        private Toolbar         toolbar;
        private Sequencer       sequencer;
        private PianoRoll       pianoRoll;
        private ProjectExplorer projectExplorer;
        private NavigationBar   navigationBar;

        public Toolbar         ToolBar         => toolbar;
        public Sequencer       Sequencer       => sequencer;
        public PianoRoll       PianoRoll       => pianoRoll;
        public ProjectExplorer ProjectExplorer => projectExplorer;
        public NavigationBar   NavigationBar   => navigationBar;

        public GLControl[] Controls => controls;
        public bool IsLandscape => width > height;

        public FamiStudioControls(FamiStudioForm parent)
        {
            toolbar         = new Toolbar();
            sequencer       = new Sequencer();
            pianoRoll       = new PianoRoll();
            projectExplorer = new ProjectExplorer();
            navigationBar   = new NavigationBar();

            controls[0] = toolbar;
            controls[1] = sequencer;
            controls[2] = pianoRoll;
            controls[3] = projectExplorer;
            controls[4] = navigationBar;

            navigationBar.PianoRollClicked += NavigationBar_PianoRollClicked;
            navigationBar.SequencerClicked += NavigationBar_SequencerClicked;
            navigationBar.ProjectExplorerClicked += NavigationBar_ProjectExplorerClicked;

            activeControl = pianoRoll;

            foreach (var ctrl in controls)
                ctrl.ParentForm = parent;
        }

        private void NavigationBar_SequencerClicked()
        {
            TransitionToControl(sequencer);
        }

        private void NavigationBar_PianoRollClicked()
        {
            TransitionToControl(pianoRoll);
        }

        private void NavigationBar_ProjectExplorerClicked()
        {
            TransitionToControl(projectExplorer);
        }

        private void TransitionToControl(GLControl ctrl)
        {
            if (activeControl != ctrl)
            {
                transitionControl = ctrl;
                transitionTimer = 1.0f;
            }
        }

        public void Resize(int w, int h)
        {
            width  = w;
            height = h;

            UpdateLayout();
        }

        private void UpdateLayout()
        {
            var landscape = IsLandscape;
            var navSize = navigationBar.DesiredSize;
            var toolLayoutSize = toolbar.LayoutSize;

            // Toolbar will be resized every frame anyway.
            if (landscape)
            {
                activeControl.Move(navSize, toolLayoutSize, width - navSize, height - toolLayoutSize);
                navigationBar.Move(0, 0, navSize, height);
            }
            else
            {
                activeControl.Move(0, toolLayoutSize, width, height - navSize - toolLayoutSize);
                navigationBar.Move(0, height - navSize, width, navSize);
            }
        }

        private void UpdateToolbar()
        {
            var navSize = navigationBar.DesiredSize;
            var toolActualSize = toolbar.DesiredSize;

            if (IsLandscape)
                toolbar.Move(navSize, 0, width - navSize, toolActualSize, false);
            else
                toolbar.Move(0, 0, width, toolActualSize, false);
        }

        public GLControl GetControlAtCoord(int formX, int formY, out int ctrlX, out int ctrlY)
        {
            // DROIDTODO : Only allow picking active control for piano roll / seq / project explorer.
            foreach (var ctrl in controls)
            {
                ctrlX = formX - ctrl.Left;
                ctrlY = formY - ctrl.Top;

                if (ctrlX >= 0 &&
                    ctrlY >= 0 &&
                    ctrlX <  ctrl.Width &&
                    ctrlY <  ctrl.Height)
                {
                    return ctrl;
                }
            }

            ctrlX = 0;
            ctrlY = 0;
            return null;
        }

        public void Invalidate()
        {
            foreach (var ctrl in controls)
                ctrl.Invalidate();
        }

        GLBrush debugBrush;

        private void RedrawControl(GLControl ctrl)
        {
            if (debugBrush == null)
                debugBrush = new GLBrush(System.Drawing.Color.SpringGreen);

            gfx.BeginDrawControl(new System.Drawing.Rectangle(ctrl.Left, ctrl.Top, ctrl.Width, ctrl.Height), height);

            var t0 = DateTime.Now;
            {
                ctrl.Render(gfx);
                ctrl.Validate();
            }
            var t1 = DateTime.Now;

            var cmd = gfx.CreateCommandList();
            cmd.DrawText($"{(t1 - t0).TotalMilliseconds}", ThemeBase.FontBigBold, 10, 10, debugBrush);
            gfx.DrawCommandList(cmd);

            gfx.EndDrawControl();
        }

        private void RenderOverlay()
        {
            gfx.BeginDrawControl(new System.Drawing.Rectangle(0, 0, width, height), height);

            var cmd = gfx.CreateCommandList();

            if (toolbar.ExpandRatio > 0.001f)
            {
                var size = MobileUtils.ComputeIdealButtonSize(width, height);
                var brush = gfx.CreateVerticalGradientBrush(
                    0, size,
                    System.Drawing.Color.FromArgb((byte)(224 * toolbar.ExpandRatio), 0, 0, 0),
                    System.Drawing.Color.FromArgb((byte)(128 * toolbar.ExpandRatio), 0, 0, 0));

                cmd.FillRectangle(toolbar.Left, toolbar.Bottom, toolbar.Right, height, brush);
            }

            if (transitionTimer > 0.0f)
            {
                var alpha = (byte)((1.0f - Math.Abs(transitionTimer - 0.5f) * 2) * 255);
                var brush = gfx.CreateSolidBrush(System.Drawing.Color.FromArgb(alpha, ThemeBase.DarkGreyFillColor1));

                cmd.FillRectangle(activeControl.Left, activeControl.Top, activeControl.Right, activeControl.Bottom, brush);
            }

            cmd.DrawLine(toolbar.Left, toolbar.Bottom, toolbar.Right, toolbar.Bottom, theme.BlackBrush, 3.0f);

            if (IsLandscape)
                cmd.DrawLine(navigationBar.Right, navigationBar.Top, navigationBar.Right, navigationBar.Bottom, theme.BlackBrush, 3.0f);
            else
                cmd.DrawLine(navigationBar.Right, navigationBar.Top, navigationBar.Left, navigationBar.Top, theme.BlackBrush, 3.0f);

            gfx.DrawCommandList(cmd);
            gfx.EndDrawControl();
        }

        private void UpdateTimeDelta()
        {
            if (lastTime == DateTime.MinValue)
                lastTime = DateTime.Now;

            var currTime = DateTime.Now;

            timeDelta = Math.Min(0.25f, (float)(currTime - lastTime).TotalSeconds);
            lastTime = currTime;
        }

        private void UpdateTransition()
        {
            if (transitionTimer > 0.0f)
            {
                var prevTimer = transitionTimer;
                transitionTimer = Math.Max(0.0f, transitionTimer - timeDelta * 6);

                if (prevTimer > 0.5f && transitionTimer <= 0.5f)
                {
                    activeControl = transitionControl;
                    transitionControl = null;
                    UpdateLayout();
                }
            }
        }

        public bool Redraw()
        {
            UpdateTimeDelta();
            UpdateTransition();
            UpdateToolbar();

            gfx.BeginDrawFrame();
            {
                RedrawControl(activeControl);
                RedrawControl(navigationBar);
                RedrawControl(toolbar);
                RenderOverlay();
            }
            gfx.EndDrawFrame();

            return false;
        }

        public void InitializeGL(IGL10 gl)
        {
            gfx = new GLGraphics(gl);
            theme = GLTheme.CreateResourcesForGraphics(gfx);
            foreach (var ctrl in controls)
                ctrl.RenderInitialized(gfx);
        }
    }
}
