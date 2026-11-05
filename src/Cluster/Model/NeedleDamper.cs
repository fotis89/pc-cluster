namespace Cluster.Model;

/// <summary>
/// A slightly under-damped spring: the needle follows each sample with a short swing, then rests.
/// </summary>
public sealed class NeedleDamper
{
    const double Omega = 11;        // natural frequency, rad/s
    const double Zeta = 0.62;       // damping ratio (< 1 gives a small overshoot)
    const double MaxStep = 1.0 / 30; // integrate in small steps so long frames stay stable
    const double RestDistance = 0.0008, RestSpeed = 0.004;

    public double Position { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; set; }

    public NeedleDamper(double position = 0)
    {
        Position = Target = position;
    }

    public bool AtRest => Math.Abs(Target - Position) < RestDistance && Math.Abs(Velocity) < RestSpeed;

    /// <summary>Advances by <paramref name="seconds"/>. Returns true while the needle is still moving.</summary>
    public bool Step(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, 0.25);
        while (seconds > 0)
        {
            double dt = Math.Min(seconds, MaxStep);
            double accel = Omega * Omega * (Target - Position) - 2 * Zeta * Omega * Velocity;
            Velocity += accel * dt;   // semi-implicit Euler
            Position += Velocity * dt;
            seconds -= dt;
        }
        if (AtRest)
        {
            Position = Target;
            Velocity = 0;
            return false;
        }
        return true;
    }
}
