using System;
using UnityEngine;

namespace BallisticSniper
{
    public enum GameScreen
    {
        Menu,
        Help,
        Briefing,
        Playing,
        Flight,
        Result,
        Summary,
        Paused,
        Cinematic
    }

    public enum Difficulty
    {
        Cadet,
        Shooter,
        Expert
    }

    public enum CampaignMode
    {
        Range,
        Operations
    }

    public enum WeaponKind
    {
        Ranger308,
        Vektor65,
        Titan338
    }

    public enum OperationKind
    {
        Conversation,
        HotelWindow,
        Rooftop
    }

    public enum TargetMotion
    {
        Static,
        Slide,
        Pendulum,
        Bob
    }

    public enum TargetKind
    {
        Steel = 0,
        GlassBottle = 1,
        ClayJug = 2,
        Cans = 3,
        WoodenCrate = 4,
        Watermelon = 5,
        ExplosiveBarrel = 6
    }

    [Serializable]
    public struct StageDefinition
    {
        public string Name;
        public string Note;
        public int RangeMetres;
        public TargetKind[] Targets;
        public float[] HeightMil;
        public TargetMotion[] Motions;

        public StageDefinition(
            string name,
            string note,
            int rangeMetres,
            TargetKind[] targets,
            float[] heightMil,
            TargetMotion[] motions)
        {
            Name = name;
            Note = note;
            RangeMetres = rangeMetres;
            Targets = targets;
            HeightMil = heightMil;
            Motions = motions;
        }
    }

    [Serializable]
    public struct WeaponDefinition
    {
        public WeaponKind Kind;
        public string Name;
        public string Calibre;
        public string Role;
        public double MuzzleVelocity;
        public double DragRate;
        public double WindDragFactor;
        public float RecoilMultiplier;
        public float RagdollImpulse;

        public WeaponDefinition(
            WeaponKind kind,
            string name,
            string calibre,
            string role,
            double muzzleVelocity,
            double dragRate,
            double windDragFactor,
            float recoilMultiplier,
            float ragdollImpulse)
        {
            Kind = kind;
            Name = name;
            Calibre = calibre;
            Role = role;
            MuzzleVelocity = muzzleVelocity;
            DragRate = dragRate;
            WindDragFactor = windDragFactor;
            RecoilMultiplier = recoilMultiplier;
            RagdollImpulse = ragdollImpulse;
        }
    }

    [Serializable]
    public struct OperationDefinition
    {
        public string Name;
        public string Note;
        public string TargetDescription;
        public string Complication;
        public int RangeMetres;
        public int Shots;
        public OperationKind Kind;

        public OperationDefinition(
            string name,
            string note,
            string targetDescription,
            string complication,
            int rangeMetres,
            int shots,
            OperationKind kind)
        {
            Name = name;
            Note = note;
            TargetDescription = targetDescription;
            Complication = complication;
            RangeMetres = rangeMetres;
            Shots = shots;
            Kind = kind;
        }
    }

    public static class GameRules
    {
        public const int Stages = 5;
        public const int TargetsPerStage = 5;
        public const int ShotsPerStage = 8;
        public const int CampaignTargets = Stages * TargetsPerStage;
        public const int CampaignDestructibles = 20;
        public const int CampaignMaxScore = 975;
        public const int OperationStages = 3;
        public const int OperationMaxScore = 300;

        public static readonly int[] ZoomLevels = { 4, 6, 8, 12, 16 };
        public static readonly float[] LanesMil = { -6f, -3f, 0f, 3f, 6f };

        public static readonly WeaponDefinition[] Weapons =
        {
            new WeaponDefinition(
                WeaponKind.Ranger308,
                "RANGER M24",
                ".308 WIN",
                "Сбалансированная",
                820.0,
                0.34,
                1.15,
                1.00f,
                7.5f),
            new WeaponDefinition(
                WeaponKind.Vektor65,
                "VEKTOR 6.5",
                "6.5 CM",
                "Настильная • мягкая отдача",
                900.0,
                0.29,
                1.02,
                0.72f,
                6.2f),
            new WeaponDefinition(
                WeaponKind.Titan338,
                "TITAN LR",
                ".338 LM",
                "Тяжёлая • максимальный импульс",
                910.0,
                0.24,
                0.92,
                1.34f,
                11.5f)
        };

        public static readonly string[] CinematicNames =
        {
            "CHASE CAM", "SIDE TRACK", "LOW ANGLE", "TOP DOWN", "ORBIT LEFT",
            "ORBIT RIGHT", "TARGET POV", "BULLET ROLL", "LONG LENS", "REVERSE DOLLY",
            "RIDGE CAM", "PARALLEL CAM", "IMPACT MACRO", "CRANE CAM"
        };

        public static readonly StageDefinition[] StageDefinitions =
        {
            new StageDefinition(
                "РАССВЕТНЫЙ ПОЛИГОН",
                "Статичные цели • знакомство с материалами",
                200,
                new[] { TargetKind.GlassBottle, TargetKind.ClayJug, TargetKind.Steel, TargetKind.Cans, TargetKind.ExplosiveBarrel },
                new[] { 0.65f, -0.55f, 0.15f, -0.70f, 0.48f },
                new[] { TargetMotion.Static, TargetMotion.Static, TargetMotion.Static, TargetMotion.Static, TargetMotion.Static }),
            new StageDefinition(
                "ДОЛИНА ВЕТРА",
                "Скользящие цели • ветер между холмами",
                350,
                new[] { TargetKind.WoodenCrate, TargetKind.Watermelon, TargetKind.Steel, TargetKind.ClayJug, TargetKind.ExplosiveBarrel },
                new[] { -0.45f, 0.62f, 0.10f, -0.58f, 0.42f },
                new[] { TargetMotion.Slide, TargetMotion.Static, TargetMotion.Static, TargetMotion.Bob, TargetMotion.Static }),
            new StageDefinition(
                "КАМЕННЫЙ КАНЬОН",
                "Маятники • пыль и дальняя дистанция",
                500,
                new[] { TargetKind.GlassBottle, TargetKind.WoodenCrate, TargetKind.Steel, TargetKind.Watermelon, TargetKind.ExplosiveBarrel },
                new[] { 0.58f, -0.62f, 0.08f, 0.52f, -0.38f },
                new[] { TargetMotion.Static, TargetMotion.Pendulum, TargetMotion.Slide, TargetMotion.Static, TargetMotion.Static }),
            new StageDefinition(
                "ПРОМЫШЛЕННЫЙ ДВОР",
                "Смешанное движение • цепная реакция",
                700,
                new[] { TargetKind.Cans, TargetKind.ClayJug, TargetKind.Steel, TargetKind.WoodenCrate, TargetKind.ExplosiveBarrel },
                new[] { -0.52f, 0.55f, 0.05f, -0.62f, 0.40f },
                new[] { TargetMotion.Slide, TargetMotion.Pendulum, TargetMotion.Slide, TargetMotion.Bob, TargetMotion.Static }),
            new StageDefinition(
                "АЛЬПИЙСКИЙ РУБЕЖ",
                "Все типы движения • финальная проверка",
                900,
                new[] { TargetKind.Watermelon, TargetKind.GlassBottle, TargetKind.Steel, TargetKind.Cans, TargetKind.ExplosiveBarrel },
                new[] { 0.52f, -0.58f, 0.02f, 0.58f, -0.42f },
                new[] { TargetMotion.Pendulum, TargetMotion.Slide, TargetMotion.Slide, TargetMotion.Bob, TargetMotion.Static })
        };

        public static readonly OperationDefinition[] OperationDefinitions =
        {
            new OperationDefinition(
                "ТИХАЯ ВСТРЕЧА",
                "Цель беседует на открытой террасе",
                "Бордовый пиджак • светлая рубашка",
                "Собеседник жестикулирует и периодически перекрывает линию огня. Гражданских не задеть.",
                280,
                4,
                OperationKind.Conversation),
            new OperationDefinition(
                "ОКНО ОТЕЛЯ",
                "Наблюдение за номером на верхнем этаже",
                "Тёмно-зелёная куртка • у окна",
                "Рама скрывает корпус, цель отходит от окна, сотрудник номера проходит перед ней.",
                420,
                4,
                OperationKind.HotelWindow),
            new OperationDefinition(
                "КРЫША ТЕРМИНАЛА",
                "Финальная цель под охраной",
                "Песочный плащ • крыша терминала",
                "Парапет закрывает ноги, цель перемещается между охраной и вентиляционным блоком.",
                610,
                5,
                OperationKind.Rooftop)
        };

        public static WeaponDefinition Weapon(int index)
        {
            return Weapons[Mathf.Clamp(index, 0, Weapons.Length - 1)];
        }

        public static int SteelScore(float horizontalErrorMetres, float verticalErrorMetres)
        {
            float radial = Mathf.Sqrt(
                horizontalErrorMetres * horizontalErrorMetres +
                verticalErrorMetres * verticalErrorMetres);
            if (radial <= 0.10f) return 10;
            if (radial <= 0.25f) return 7;
            if (radial <= 0.48f) return 4;
            return 0;
        }

        public static int DestructionScore(int streak)
        {
            return 15 + Mathf.Min(2, Mathf.Max(0, streak - 1)) * 5;
        }

        public static string TargetName(TargetKind kind)
        {
            switch (kind)
            {
                case TargetKind.GlassBottle: return "СТЕКЛЯННАЯ БУТЫЛКА";
                case TargetKind.ClayJug: return "ГЛИНЯНЫЙ КУВШИН";
                case TargetKind.Cans: return "ПИРАМИДА БАНОК";
                case TargetKind.WoodenCrate: return "ДЕРЕВЯННЫЙ ЯЩИК";
                case TargetKind.Watermelon: return "АРБУЗ";
                case TargetKind.ExplosiveBarrel: return "ВЗРЫВНАЯ БОЧКА";
                default: return "СТАЛЬНАЯ МИШЕНЬ";
            }
        }

        public static Vector2 TargetSize(TargetKind kind)
        {
            switch (kind)
            {
                case TargetKind.GlassBottle: return new Vector2(0.30f, 0.72f);
                case TargetKind.ClayJug: return new Vector2(0.50f, 0.58f);
                case TargetKind.Cans: return new Vector2(0.60f, 0.68f);
                case TargetKind.WoodenCrate: return new Vector2(0.72f, 0.66f);
                case TargetKind.Watermelon: return new Vector2(0.68f, 0.50f);
                case TargetKind.ExplosiveBarrel: return new Vector2(0.62f, 0.88f);
                default: return new Vector2(0.96f, 0.96f);
            }
        }
    }
}
