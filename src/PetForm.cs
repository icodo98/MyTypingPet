using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace MyTypingPet
{
    // 화면에 떠 있는 펫 창. 투명 배경 위에 현재 동작의 프레임을 그린다.
    class PetForm : Form
    {
        const int VK_BACK = 0x08;
        const int VK_RETURN = 0x0D;

        readonly SpriteSet sprites;
        readonly Settings settings;
        readonly EventWaitHandle bringBackSignal;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Queue<long> recentTypingKeys = new Queue<long>();   // 최근 1초간 누른 일반 키 시각
        readonly System.Windows.Forms.Timer timer;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        KeyHook hook;
        volatile bool closing;

        // 현재 동작 상태
        Mood mood = Mood.Idle;
        long moodStartMs;
        long lastKeyMs;         // 아무 키나 마지막으로 누른 시각 (쉬기 타이머용)
        long lastTypingKeyMs;   // 일반 키를 마지막으로 누른 시각 (타이핑 동작 유지용)

        // 통통 튀는 효과(스프링). squash > 0 이면 눌림, < 0 이면 늘어나며 뜀
        double squash, squashVel;
        long lastTickMs;

        Bitmap canvas;
        Bitmap shownFrame;
        bool dirty = true;

        // 메뉴 체크 표시를 갱신하려고 들고 있는 항목들
        readonly List<ToolStripMenuItem> sizeItems = new List<ToolStripMenuItem>();
        readonly List<ToolStripMenuItem> restItems = new List<ToolStripMenuItem>();
        ToolStripMenuItem onTopItem, fixedItem, autoStartItem;

        public PetForm(SpriteSet sprites, EventWaitHandle bringBackSignal)
        {
            this.sprites = sprites;
            this.bringBackSignal = bringBackSignal;
            settings = Settings.Load();

            Text = Config.AppName;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = settings.OnTop;

            menu = new ContextMenuStrip();
            BuildMenu();
            menu.Opening += (s, e) => RefreshMenuChecks();

            tray = new NotifyIcon();
            tray.Text = Config.AppName;
            tray.Icon = MakeTrayIcon();
            tray.ContextMenuStrip = menu;
            tray.MouseDoubleClick += (s, e) => BringBack();
            tray.Visible = true;

            Size = CanvasSize();
            Location = InitialLocation(Size);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 15;
            timer.Tick += (s, e) => Tick();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                hook = new KeyHook();
                hook.KeyDown += OnKey;
            }
            catch (Exception ex)
            {
                MessageBox.Show("키보드 신호를 받을 수 없어요.\n" + ex.Message, Config.AppName);
            }

            lastTickMs = lastKeyMs = moodStartMs = clock.ElapsedMilliseconds;
            timer.Start();
            Render();

            var waiter = new Thread(WaitForBringBack);
            waiter.IsBackground = true;
            waiter.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            timer.Stop();
            if (hook != null) { hook.Dispose(); hook = null; }
            SaveLocation();
            tray.Visible = false;
            tray.Dispose();
            try { bringBackSignal.Set(); } catch { }   // 대기 스레드를 깨워서 끝낸다
            base.OnFormClosing(e);
        }

        // ── 키 입력 → 동작 결정 ──────────────────────────────────────

        static bool IsModifierOrImeKey(int vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12        // Shift, Ctrl, Alt
                || (vk >= 0xA0 && vk <= 0xA5)                    // 좌/우 Shift, Ctrl, Alt
                || vk == 0x5B || vk == 0x5C                      // Windows 키
                || vk == 0x14 || vk == 0x15 || vk == 0x19;       // CapsLock, 한/영, 한자
        }

        void OnKey(int vk)
        {
            if (IsModifierOrImeKey(vk)) return;

            long now = clock.ElapsedMilliseconds;
            bool wasResting = mood == Mood.Rest || mood == Mood.Sleep;
            lastKeyMs = now;

            if (vk == VK_BACK)
            {
                if (mood != Mood.Back) SetMood(Mood.Back, now);   // 꾹 누르고 있어도 처음부터 다시 돌지 않게
            }
            else if (vk == VK_RETURN)
            {
                SetMood(Mood.Enter, now);
            }
            else
            {
                lastTypingKeyMs = now;
                recentTypingKeys.Enqueue(now);
                if (mood == Mood.Idle || wasResting) SetMood(Mood.Type, now);
            }

            if (Config.BounceOnKey)
                squashVel += wasResting ? -Config.HopOnWakeUp : Config.BounceOnType;
            dirty = true;
        }

        void Tick()
        {
            long now = clock.ElapsedMilliseconds;
            double dt = Math.Min(0.05, (now - lastTickMs) / 1000.0);
            lastTickMs = now;

            while (recentTypingKeys.Count > 0 && now - recentTypingKeys.Peek() > 1000)
                recentTypingKeys.Dequeue();

            UpdateMood(now);

            if (squash != 0 || squashVel != 0)
            {
                double acc = -Config.SpringStiffness * squash - Config.SpringDamping * squashVel;
                squashVel += acc * dt;
                squash += squashVel * dt;
                if (Math.Abs(squash) < 0.002 && Math.Abs(squashVel) < 0.02) { squash = 0; squashVel = 0; }
                dirty = true;
            }

            if (CurrentFrame(now) != shownFrame) dirty = true;
            if (dirty) Render();
        }

        void UpdateMood(long now)
        {
            bool stillTyping = recentTypingKeys.Count > 0 && now - lastTypingKeyMs <= Config.TypingHoldMs;

            switch (mood)
            {
                case Mood.Back:
                case Mood.Enter:
                    if (OneShotFinished(now)) SetMood(stillTyping ? TypingMood() : Mood.Idle, now);
                    break;

                case Mood.Type:
                case Mood.Fast:
                    if (!stillTyping) SetMood(Mood.Idle, now);
                    else
                    {
                        Mood m = TypingMood();
                        if (m != mood) SetMood(m, now);
                    }
                    break;

                case Mood.Idle:
                    if (now - lastKeyMs > settings.RestMs) SetMood(Mood.Rest, now);
                    break;

                case Mood.Rest:
                    if (OneShotFinished(now)) SetMood(Mood.Sleep, now);
                    break;
            }
        }

        Mood TypingMood()
        {
            // 한 번 fast 가 되면 조금 느려져도 바로 안 돌아가게 (깜빡임 방지)
            double threshold = mood == Mood.Fast ? Config.FastKeysPerSecond * 0.7 : Config.FastKeysPerSecond;
            return recentTypingKeys.Count >= threshold ? Mood.Fast : Mood.Type;
        }

        void SetMood(Mood m, long now)
        {
            mood = m;
            moodStartMs = now;
            dirty = true;
        }

        bool OneShotFinished(long now)
        {
            return (now - moodStartMs) * Config.Fps(mood) / 1000.0 >= sprites.Get(mood).Length;
        }

        Bitmap CurrentFrame(long now)
        {
            Bitmap[] frames = sprites.Get(mood);
            int i = (int)((now - moodStartMs) * Config.Fps(mood) / 1000.0);
            if (mood == Mood.Back || mood == Mood.Enter || mood == Mood.Rest)
                i = Math.Min(i, frames.Length - 1);   // 한 번만 재생하고 마지막 프레임에서 멈춤
            else
                i %= frames.Length;                    // 반복 재생
            return frames[i];
        }

        // ── 그리기 ──────────────────────────────────────────────────

        Size CanvasSize()
        {
            double k = settings.SizePercent / 100.0;
            Size f = sprites.FrameSize;
            // 튀어 오르거나 옆으로 퍼질 여유 공간을 조금 더 둔다
            return new Size((int)Math.Ceiling(f.Width * k * 1.2), (int)Math.Ceiling(f.Height * k * 1.3));
        }

        void Render()
        {
            if (!IsHandleCreated) return;

            Bitmap frame = CurrentFrame(clock.ElapsedMilliseconds);
            shownFrame = frame;
            dirty = false;

            Size size = CanvasSize();
            if (canvas == null || canvas.Size != size)
            {
                if (canvas != null) canvas.Dispose();
                canvas = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            }
            if (Size != size) Size = size;

            double k = settings.SizePercent / 100.0;
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                float sx = (float)(k * (1 + 0.10 * squash));
                float sy = (float)(k * (1 - 0.15 * squash));
                float lift = squash < 0 ? (float)(-squash * 0.15 * sprites.FrameSize.Height * k) : 0f;

                // 발밑(아래 가운데)을 기준점으로 늘리고 줄인다
                g.TranslateTransform(size.Width / 2f, size.Height - lift);
                g.ScaleTransform(sx, sy);
                g.DrawImage(frame, -frame.Width / 2f, -frame.Height, frame.Width, frame.Height);
            }
            Present(canvas);
        }

        // 투명도가 있는 비트맵을 창에 그대로 올린다 (배경이 비치는 펫 창의 핵심)
        void Present(Bitmap bmp)
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(memDc, hBitmap);
            try
            {
                var dst = new Native.POINT(Left, Top);
                var sz = new Native.SIZE(bmp.Width, bmp.Height);
                var src = new Native.POINT(0, 0);
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = Native.AC_SRC_OVER,
                    SourceConstantAlpha = 255,
                    AlphaFormat = Native.AC_SRC_ALPHA
                };
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref sz, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(memDc, old);
                Native.DeleteObject(hBitmap);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        Icon MakeTrayIcon()
        {
            Bitmap src = sprites.Get(Mood.Idle)[0];
            Rectangle r = OpaqueBounds(src);
            using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float scale = Math.Min(32f / r.Width, 32f / r.Height);
                    float w = r.Width * scale, h = r.Height * scale;
                    g.DrawImage(src, new RectangleF((32 - w) / 2, (32 - h) / 2, w, h), r, GraphicsUnit.Pixel);
                }
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(hIcon))
                        return (Icon)tmp.Clone();
                }
                finally
                {
                    Native.DestroyIcon(hIcon);
                }
            }
        }

        // 그림에서 투명하지 않은 부분만 감싸는 사각형 (트레이 아이콘을 꽉 차게 만들려고)
        static Rectangle OpaqueBounds(Bitmap b)
        {
            int minX = b.Width, minY = b.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                    if (b.GetPixel(x, y).A > 16)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
            if (maxX < 0) return new Rectangle(0, 0, b.Width, b.Height);
            int side = Math.Max(maxX - minX + 1, maxY - minY + 1);
            return new Rectangle((minX + maxX + 1 - side) / 2, (minY + maxY + 1 - side) / 2, side, side);
        }

        // ── 위치 ────────────────────────────────────────────────────

        Point DefaultSpot(Size s)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            return new Point(wa.Right - s.Width - 16, wa.Bottom - s.Height);   // 오른쪽 아래, 작업표시줄 위
        }

        Point InitialLocation(Size s)
        {
            if (settings.X.HasValue && settings.Y.HasValue)
            {
                var p = new Point(settings.X.Value, settings.Y.Value);
                if (IsOnAnyScreen(new Rectangle(p, s))) return p;
            }
            return DefaultSpot(s);
        }

        static bool IsOnAnyScreen(Rectangle r)
        {
            foreach (Screen sc in Screen.AllScreens)
                if (sc.WorkingArea.IntersectsWith(r)) return true;
            return false;
        }

        void SaveLocation()
        {
            settings.X = Left;
            settings.Y = Top;
            settings.Save();
        }

        void BringBack()
        {
            if (closing) return;
            Location = DefaultSpot(Size);
            if (!Visible) Show();
            TopMost = true;               // 다른 창 위로 한 번 끌어올린 뒤
            TopMost = settings.OnTop;     // 원래 설정으로 되돌림
            SaveLocation();
            Render();
        }

        void WaitForBringBack()
        {
            try
            {
                while (true)
                {
                    bringBackSignal.WaitOne();
                    if (closing) return;
                    BeginInvoke(new Action(() =>
                    {
                        BringBack();
                        tray.ShowBalloonTip(2500, Config.AppName,
                            "이미 켜져 있어요! 오른쪽 아래로 불러왔어요.", ToolTipIcon.Info);
                    }));
                }
            }
            catch { /* 종료 중 */ }
        }

        // ── 마우스 ──────────────────────────────────────────────────

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_NCHITTEST:
                    base.WndProc(ref m);
                    // 창 어디를 잡아도 제목 표시줄을 잡은 것처럼 → 끌어서 이동
                    if (settings != null && !settings.Fixed && m.Result == (IntPtr)Native.HTCLIENT)
                        m.Result = (IntPtr)Native.HTCAPTION;
                    return;
                case Native.WM_NCRBUTTONUP:
                    if (menu != null) menu.Show(Cursor.Position);
                    return;
                case Native.WM_NCLBUTTONDBLCLK:
                    return;   // 더블클릭으로 창이 최대화되지 않게
                case Native.WM_EXITSIZEMOVE:
                    base.WndProc(ref m);
                    SaveLocation();
                    return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position);   // 위치 고정 상태일 때
        }

        // ── 메뉴 ────────────────────────────────────────────────────

        void BuildMenu()
        {
            var sizeMenu = new ToolStripMenuItem("크기");
            foreach (int pct in Config.SizeChoicesPercent)
            {
                var item = new ToolStripMenuItem(pct + "%");
                item.Tag = pct;
                item.Click += (s, e) => { settings.SizePercent = pct; settings.Save(); ApplySize(); };
                sizeMenu.DropDownItems.Add(item);
                sizeItems.Add(item);
            }

            var restMenu = new ToolStripMenuItem("쉬는 타이밍");
            foreach (int ms in Config.RestChoicesMs)
            {
                var item = new ToolStripMenuItem("멈추고 " + (ms / 1000.0).ToString("0.#") + "초 뒤");
                item.Tag = ms;
                item.Click += (s, e) => { settings.RestMs = ms; settings.Save(); };
                restMenu.DropDownItems.Add(item);
                restItems.Add(item);
            }

            onTopItem = new ToolStripMenuItem("항상 위에 표시", null, (s, e) =>
            {
                settings.OnTop = !settings.OnTop;
                TopMost = settings.OnTop;
                settings.Save();
            });
            fixedItem = new ToolStripMenuItem("위치 고정 (마우스로 안 움직임)", null, (s, e) =>
            {
                settings.Fixed = !settings.Fixed;
                settings.Save();
            });
            autoStartItem = new ToolStripMenuItem("Windows 시작 시 자동 실행", null, (s, e) =>
            {
                try { AutoStart.Enabled = !AutoStart.Enabled; }
                catch (Exception ex) { MessageBox.Show(ex.Message, Config.AppName); }
            });

            menu.Items.Add(sizeMenu);
            menu.Items.Add(restMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(onTopItem);
            menu.Items.Add(fixedItem);
            menu.Items.Add(autoStartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("오른쪽 아래로 불러오기", null, (s, e) => BringBack()));
            menu.Items.Add(new ToolStripMenuItem("정보", null, (s, e) =>
                MessageBox.Show(Config.AboutText, Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("종료", null, (s, e) => Close()));
        }

        void RefreshMenuChecks()
        {
            foreach (ToolStripMenuItem item in sizeItems) item.Checked = (int)item.Tag == settings.SizePercent;
            foreach (ToolStripMenuItem item in restItems) item.Checked = (int)item.Tag == settings.RestMs;
            onTopItem.Checked = settings.OnTop;
            fixedItem.Checked = settings.Fixed;
            try { autoStartItem.Checked = AutoStart.Enabled; } catch { autoStartItem.Checked = false; }
        }

        void ApplySize()
        {
            // 발밑 가운데를 기준으로 크기를 바꾼다
            Size old = Size, neu = CanvasSize();
            int centerX = Left + old.Width / 2, bottom = Top + old.Height;
            Bounds = new Rectangle(centerX - neu.Width / 2, bottom - neu.Height, neu.Width, neu.Height);
            if (!IsOnAnyScreen(Bounds)) Location = DefaultSpot(neu);
            SaveLocation();
            Render();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (canvas != null) canvas.Dispose();
                if (timer != null) timer.Dispose();
                if (menu != null) menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
