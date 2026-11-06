namespace MyTypingPet
{
    // ─────────────────────────────────────────────────────────────
    //  내 캐릭터에 맞게 바꿔도 되는 값은 전부 여기 모아 두었어요.
    // ─────────────────────────────────────────────────────────────
    static class Config
    {
        // 메뉴·트레이·알림창에 보이는 이름
        public const string AppName = "타이핑 펫";

        // 설정 저장(레지스트리)·중복 실행 방지에 쓰는 ID. 영문/숫자만 쓰세요.
        // 다른 펫과 같이 켜고 싶으면 펫마다 다르게 지어 주세요.
        public const string AppId = "MyTypingPet";

        public const string Version = "1.0";

        // "정보" 메뉴에 나오는 글
        public const string AboutText =
            AppName + " v" + Version + "\n\n" +
            "키보드를 누를 때마다 움직이는 데스크톱 펫이에요.\n" +
            "\"키가 눌렸다\"는 신호만 쓰고, 어떤 글자를 쳤는지는 저장하거나 보내지 않아요.\n\n" +
            "만든 사람: (여기에 이름을 적어 주세요)";

        // 1초에 이만큼 이상 치면 '빠르게 치기(fast)' 동작으로 바뀌어요.
        public const double FastKeysPerSecond = 6.0;

        // 마지막 키를 누른 뒤 이 시간(ms) 동안은 타이핑 동작을 계속해요.
        public const int TypingHoldMs = 350;

        // 크기 메뉴에 나오는 선택지(%)와 기본값
        public static readonly int[] SizeChoicesPercent = { 50, 75, 100, 150, 200 };
        public const int DefaultSizePercent = 100;

        // 쉬는 타이밍 메뉴에 나오는 선택지(ms)와 기본값
        public static readonly int[] RestChoicesMs = { 500, 1500, 3000, 5000 };
        public const int DefaultRestMs = 1500;

        // 키를 누를 때 몸이 살짝 눌렸다 튀어 오르는 효과
        public const bool BounceOnKey = true;
        public const double BounceOnType = 8;     // 클수록 많이 눌림
        public const double HopOnWakeUp = 14;     // 쉬다가 깰 때 깡총 뛰는 세기
        public const double SpringStiffness = 300;
        public const double SpringDamping = 14;

        // 동작별 애니메이션 속도(초당 프레임 수)
        public static double Fps(Mood mood)
        {
            switch (mood)
            {
                case Mood.Idle: return 3;
                case Mood.Type: return 10;
                case Mood.Fast: return 16;
                case Mood.Back: return 14;
                case Mood.Enter: return 12;
                case Mood.Rest: return 8;
                default: return 2;   // Sleep
            }
        }
    }
}
