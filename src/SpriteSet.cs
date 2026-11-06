using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MyTypingPet
{
    // sprites 폴더 아래 폴더 이름과 1:1로 대응해요. (idle, type, fast, back, enter, rest, sleep)
    enum Mood { Idle, Type, Fast, Back, Enter, Rest, Sleep }

    // exe 안에 넣어 둔 PNG 프레임들을 동작별로 읽어 오는 클래스
    class SpriteSet
    {
        readonly Dictionary<Mood, Bitmap[]> frames = new Dictionary<Mood, Bitmap[]>();
        readonly HashSet<Mood> provided = new HashSet<Mood>();

        public Size FrameSize { get; private set; }

        public Bitmap[] Get(Mood mood) { return frames[mood]; }

        // 그 동작용 그림을 실제로 넣었는지 (없으면 다른 동작 그림으로 대신해요)
        public bool Has(Mood mood) { return provided.Contains(mood); }

        public static SpriteSet Load()
        {
            var set = new SpriteSet();
            Assembly asm = Assembly.GetExecutingAssembly();
            string[] names = asm.GetManifestResourceNames();

            var raw = new Dictionary<Mood, Bitmap[]>();
            foreach (Mood mood in Enum.GetValues(typeof(Mood)))
            {
                string prefix = "sprites." + mood.ToString().ToLowerInvariant() + ".";
                raw[mood] = names
                    .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                             && n.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    // 길이 → 이름 순: 1.png, 2.png, 10.png 처럼 숫자 이름도 순서대로
                    .OrderBy(n => n.Length).ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Select(n => LoadBitmap(asm, n))
                    .ToArray();
                if (raw[mood].Length > 0) set.provided.Add(mood);
            }

            Bitmap[] idle = raw[Mood.Idle].Length > 0 ? raw[Mood.Idle]
                          : raw[Mood.Type].Length > 0 ? new[] { raw[Mood.Type][0] }
                          : null;
            if (idle == null)
                throw new InvalidOperationException(
                    "sprites\\idle 이나 sprites\\type 폴더에 PNG가 하나도 없어요.\n그림을 넣고 build.bat 으로 다시 빌드해 주세요.");

            Bitmap[] type = Or(raw[Mood.Type], idle);
            Bitmap[] rest = Or(raw[Mood.Rest], idle);
            set.frames[Mood.Idle] = idle;
            set.frames[Mood.Type] = type;
            set.frames[Mood.Fast] = Or(raw[Mood.Fast], type);
            set.frames[Mood.Back] = Or(raw[Mood.Back], type);
            set.frames[Mood.Enter] = Or(raw[Mood.Enter], type);
            set.frames[Mood.Rest] = rest;
            set.frames[Mood.Sleep] = Or(raw[Mood.Sleep], new[] { rest[rest.Length - 1] });

            int w = 0, h = 0;
            foreach (Bitmap[] list in set.frames.Values)
                foreach (Bitmap b in list) { w = Math.Max(w, b.Width); h = Math.Max(h, b.Height); }
            set.FrameSize = new Size(w, h);
            return set;
        }

        static Bitmap[] Or(Bitmap[] value, Bitmap[] fallback)
        {
            return value.Length > 0 ? value : fallback;
        }

        static Bitmap LoadBitmap(Assembly asm, string name)
        {
            // GDI+ 는 원본 스트림이 열려 있어야 해서, 32비트 비트맵으로 복사해 둔다.
            using (Stream s = asm.GetManifestResourceStream(name))
            using (var tmp = new Bitmap(s))
            {
                var copy = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(copy))
                    g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
                return copy;
            }
        }
    }
}
