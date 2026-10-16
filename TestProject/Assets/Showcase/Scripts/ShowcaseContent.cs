using UnityEngine;

namespace Showcase
{
    // 게임 코드와 애셋 생성 스크립트가 같이 쓰는 경로와 맵 배치
    public static class ShowcaseContent
    {
        // Built-in: 앱에 포함
        public const string Cannon = "Showcase/Towers/Cannon";
        public const string Crossbow = "Showcase/Towers/Crossbow";
        public const string Grunt = "Showcase/Enemies/Grunt";
        public const string Runner = "Showcase/Enemies/Runner";
        public const string Shell = "Showcase/Projectiles/Shell";
        public const string Bolt = "Showcase/Projectiles/Bolt";
        public const string Burst = "Showcase/Effects/Burst";
        public const string Meadow = "Showcase/Maps/Meadow";
        public const string Slot = "Showcase/Maps/Slot";

        // Download: 처음 쓸 때 받음
        public const string DlcFolder = "ShowcaseDLC";
        public const string Desert = "ShowcaseDLC/Maps/Desert";
        public const string Titan = "ShowcaseDLC/Boss/Titan";
        public const string BattleTheme = "ShowcaseDLC/Audio/BattleTheme";

        public const float MapSize = 40f;

        // 적이 걷는 길. y는 무시함
        public static readonly Vector3[] Path =
        {
            new(-22f, 0f, 12f),
            new(-8f, 0f, 12f),
            new(-8f, 0f, -6f),
            new(6f, 0f, -6f),
            new(6f, 0f, 10f),
            new(14f, 0f, 10f),
            new(14f, 0f, -14f),
            new(22f, 0f, -14f),
        };

        public static readonly Vector3[] Slots =
        {
            new(-14f, 0f, 7f),
            new(-3f, 0f, 16f),
            new(-3f, 0f, 3f),
            new(-13f, 0f, -2f),
            new(1f, 0f, -11f),
            new(10f, 0f, 3f),
            new(10f, 0f, 15f),
            new(19f, 0f, -6f),
            new(9f, 0f, -18f),
        };

        public const float PathHalfWidth = 1.6f;
    }
}
