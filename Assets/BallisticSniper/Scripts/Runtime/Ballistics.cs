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
    /// Compact exterior-ballistics model tuned for a .308 Win match load.
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
            double time = TimeOfFlight(rangeMetres);
            double zeroTime = TimeOfFlight(ZeroDistance);
            double zeroAngle = 0.5 * Gravity * zeroTime * zeroTime / ZeroDistance;
            double drop = 0.5 * Gravity * time * time - rangeMetres * zeroAngle;
            double aerodynamicLag = Math.Max(0.0, time - rangeMetres / MuzzleVelocity);
            double windDrift = crossWindMetresPerSecond * aerodynamicLag * WindDragFactor;

            return new BallisticSolution(
                time,
                drop,
                windDrift,
                drop / rangeMetres * 1000.0,
                windDrift / rangeMetres * 1000.0);
        }

        public static double TimeOfFlight(double rangeMetres)
        {
            double ratio = 1.0 - DragRate * rangeMetres / MuzzleVelocity;
            ratio = Math.Max(0.08, ratio);
            return -Math.Log(ratio) / DragRate;
        }

        public static float VisualFlightSeconds(double rangeMetres)
        {
            // A single 1.25x readable slow-motion factor retains the real TOF ratios.
            return (float)(TimeOfFlight(rangeMetres) * 1.25);
        }
    }
}
