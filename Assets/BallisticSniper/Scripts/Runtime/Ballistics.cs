using System;

namespace BallisticSniper
{
    public readonly struct BallisticSolution
    {
        public readonly double TimeSeconds;
        public readonly double DropMetres;
        public readonly double WindDriftMetres;
        public readonly double ElevationMil;
        public readonly double WindMil;

        public BallisticSolution(
            double timeSeconds,
            double dropMetres,
            double windDriftMetres,
            double elevationMil,
            double windMil)
        {
            TimeSeconds = timeSeconds;
            DropMetres = dropMetres;
            WindDriftMetres = windDriftMetres;
            ElevationMil = elevationMil;
            WindMil = windMil;
        }
    }

    /// <summary>
    /// Compact exterior-ballistics model shared by the selectable rifle loads.
    /// Distance is metres, wind is m/s, time is seconds and angular correction is MIL.
    /// </summary>
    public static class Ballistics
    {
        public const double MuzzleVelocity = 820.0;
        public const double ZeroDistance = 100.0;
        public const double Gravity = 9.80665;
        private const double DragRate = 0.34;
        private const double WindDragFactor = 1.15;

        public static BallisticSolution Solve(double rangeMetres, double crossWindMetresPerSecond)
        {
            return Solve(rangeMetres, crossWindMetresPerSecond, GameRules.Weapons[0]);
        }

        public static BallisticSolution Solve(
            double rangeMetres,
            double crossWindMetresPerSecond,
            WeaponDefinition weapon)
        {
            double time = TimeOfFlight(rangeMetres, weapon);
            double zeroTime = TimeOfFlight(ZeroDistance, weapon);
            double zeroAngle = 0.5 * Gravity * zeroTime * zeroTime / ZeroDistance;
            double drop = 0.5 * Gravity * time * time - rangeMetres * zeroAngle;
            double aerodynamicLag = Math.Max(0.0, time - rangeMetres / weapon.MuzzleVelocity);
            double windDrift = crossWindMetresPerSecond * aerodynamicLag * weapon.WindDragFactor;

            return new BallisticSolution(
                time,
                drop,
                windDrift,
                drop / rangeMetres * 1000.0,
                windDrift / rangeMetres * 1000.0);
        }

        public static double HorizontalImpact(
            double opticalAxisXMetres,
            double rangeMetres,
            double crossWindMetresPerSecond,
            double windageDialMil)
        {
            return HorizontalImpact(
                opticalAxisXMetres,
                rangeMetres,
                crossWindMetresPerSecond,
                windageDialMil,
                GameRules.Weapons[0]);
        }

        public static double HorizontalImpact(
            double opticalAxisXMetres,
            double rangeMetres,
            double crossWindMetresPerSecond,
            double windageDialMil,
            WeaponDefinition weapon)
        {
            BallisticSolution solution = Solve(rangeMetres, crossWindMetresPerSecond, weapon);
            return opticalAxisXMetres + solution.WindDriftMetres +
                   windageDialMil * rangeMetres / 1000.0;
        }

        public static double TimeOfFlight(double rangeMetres)
        {
            return TimeOfFlight(rangeMetres, GameRules.Weapons[0]);
        }

        public static double TimeOfFlight(double rangeMetres, WeaponDefinition weapon)
        {
            double ratio = 1.0 - weapon.DragRate * rangeMetres / weapon.MuzzleVelocity;
            ratio = Math.Max(0.08, ratio);
            return -Math.Log(ratio) / weapon.DragRate;
        }

        public static float VisualFlightSeconds(double rangeMetres)
        {
            // A single 1.25x readable slow-motion factor retains the real TOF ratios.
            return (float)(TimeOfFlight(rangeMetres) * 1.25);
        }

        public static float VisualFlightSeconds(double rangeMetres, WeaponDefinition weapon)
        {
            return (float)(TimeOfFlight(rangeMetres, weapon) * 1.25);
        }
    }
}
