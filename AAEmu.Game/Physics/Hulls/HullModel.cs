#nullable enable

using System.Numerics;

using AAEmu.Game.Models.Game.Models;

namespace AAEmu.Game.Physics.Hulls;

/// <summary>
/// The coefficients fitted to native voyages and the ship row's mass and water properties. Rates are per wall second;
/// <see cref="PhysicsTimeScale"/> turns the body velocities into motion. Steering, throttle and rpm are fractions of
/// full scale (the wire's 127).
/// </summary>
/// <remarks>
/// <para>What native does, read off its type-4 reports (plan slice 7, "Hulls"): the hull's own steering slews to
/// the helm's like a servo (gain <see cref="SteerGain"/>, rate cap <see cref="SteerRate"/>, acceleration cap
/// <see cref="SteerAccel"/>); its throttle follows the helm's with a <see cref="ThrottleLag"/> s lag; the engine
/// rpm spins up at <see cref="RpmUp"/> per second with power and drops at <see cref="RpmDown"/> in neutral.
/// Thrust is rpm × (<see cref="ThrustAtRest"/> − <see cref="ThrustFade"/> · u) ahead. Astern thrust drops toward the
/// model's reverse speed limit. The rudder reverses its effect when backing. Positions advance by only
/// <see cref="PhysicsTimeScale"/> of the reported velocity per wall second (yaw the same): the native physics clock
/// runs 3.6 % slow, and the reported velocities
/// are per physics second.</para>
/// <para>Fitted: ship model 3 (Harpoon Clipper, 15 t) and ship model 6 (Eznan Cutter, 45 t). The two differ more
/// than any <c>ship_models</c> column predicts: model 6 tops out at 9.1 m/s against its <c>velocity</c> of 12.6
/// (model 3: 13.6 against 14), steers its rudder at 0.78/s against 1.34/s with the same <c>steer_vel</c>, turns at a
/// tenth of model 3's rate and floats 0.43 m above the ocean where model 3 floats 0.41 m below. So an unfitted model
/// gets the fitted one nearest in mass (<see cref="For"/>) rather than a scaled guess; that stays a guess until it is
/// traced.</para>
/// </remarks>
public sealed record HullModel
{
    /// <summary>Native physics seconds per wall second, measured on every voyage (positions and yaw alike).</summary>
    public const float PhysicsTimeScale = 0.964f;

    public float ThrustAtRest { get; init; }
    public float ThrustFade { get; init; }
    public float ReverseThrust { get; init; }
    public float ReverseSpeedLimit { get; private init; } = 5f;
    public float SurgeDrag { get; init; }
    public float SurgeDrag2 { get; init; }
    public float SurgeYawSway { get; init; }
    public float SwayDrag { get; init; }
    public float SwayDrag2 { get; init; }
    public float SwayYawSurge { get; init; }
    public float SwaySteerSurge { get; init; }
    public float YawSteer { get; init; }
    public float YawSteerRpm { get; init; }
    public float YawSteerSurge { get; init; }
    public float YawSteerSurge2 { get; init; }
    public float YawDrag { get; init; }
    public float YawDragSurge { get; init; }
    public float YawDrag2 { get; init; }
    public float RpmUp { get; init; }
    public float RpmDown { get; init; }
    public float SteerGain { get; init; }
    public float SteerRate { get; init; }
    public float SteerAccel { get; init; }
    public float ThrottleLag { get; init; }

    /// <summary>Metres the resting hull floats below the ocean level (negative: above it).</summary>
    public float Draft { get; init; }
    /// <summary>Metres the hull rises per m/s of forward speed.</summary>
    public float RisePerSpeed { get; init; }
    /// <summary>Heave spring (1/s²) and damping (1/s): a seed settles in a bob of about 1.15 s.</summary>
    public float HeaveSpring { get; init; } = 30f;
    public float HeaveDamping { get; init; } = 3.3f;

    /// <summary>Kilograms, for <c>HullImpulse</c>'s linear and angular impulses (the ship model's <c>mass</c>).</summary>
    public float Mass { get; init; }

    /// <summary>Yaw inertia per kilogram, in square metres. The reference footprint is used when the mass box is unavailable.</summary>
    public double YawInertiaPerMass { get; private init; } =
        BoxInertiaPerMass(HullShape.ReferenceSize.X * 0.5, HullShape.ReferenceSize.Y * 0.5);
    // The Harpoon Clipper's 4 x 8 x 2.5 m mass-box half extents are the fallback for old rows without a mass box.
    internal double PitchInertiaPerMass { get; private init; } = BoxInertiaPerMass(8d, 2.5d);
    internal double RollInertiaPerMass { get; private init; } = BoxInertiaPerMass(4d, 2.5d);
    internal Vector3 MassCentre { get; private init; }
    internal float WaterResistance { get; private init; }
    internal float WaterDamping { get; private init; }

    /// <summary>The <c>ship_models.id</c> this model was fitted to.</summary>
    public uint FittedShipModel { get; init; }

    /// <summary>
    /// Ship model 3, the Harpoon Clipper, from three voyages in native zone 191 (trace 191-0-20261105-173736). Two
    /// stages, as for every model: the servos fitted on the hull's reported steering, throttle and rpm (rms 4.1, 2.0 and
    /// 6.7 of 127), then the dynamics by least squares on 106 windows, each re-seeded from native's report and run 4 s
    /// on the recorded helm against native's interpolated pose — windows a ship skill's buff touched left out, every
    /// drag kept ≥ 0, reverse thrust held at the first fit's 2.563 (the windows that saw astern are buff windows now).
    /// Position p90 0.92 / 2.06 m, yaw p90 2.4 / 4.8° at +2 / +4 s. Full throttle settles at 13.6 m/s (native's fastest reports: 13.3–13.4, still climbing); waterline 99.587
    /// at rest (ocean 100), +4.5 mm per m/s.
    /// </summary>
    public static HullModel HarpoonClipper { get; } = FromFit(3, 15000f, draft: 0.413f, risePerSpeed: 0.0045f,
    [
        5.7606f, 0.1989f, 2.563f, 0.2244f, 0f, -0.866f, 0.7698f, 1.1161f, 0.152f, 0f, 0.0257f, 0.05f,
        0.616f, 20f, 0.986f, 1.336f, 0.956f, 0.197f, 0.3682f, -0.0613f, 0f, 0.0123f, -0.0024f
    ]);

    /// <summary>
    /// Ship model 6, the Eznan Cutter: the servos fitted on its reported steering, throttle and rpm (rms 1.2, 0.7 and
    /// 3.5 of 127), then the dynamics on 163 windows of its voyages in native 191 and 153 (trace
    /// 191-0-20261105-193036, 153-0-20261105-193531) with the yaw–sway coupling held at the rotating-frame terms
    /// (model 3 found them nearly by itself) and the windows a ship skill's buff touched left out. Position p90 0.86 /
    /// 2.08 m, yaw p90 0.5 / 1.4° at +2 / +4 s over 173 windows. Full throttle settles at 9.1 m/s (native 9.2–9.8); waterline 100.434
    /// at rest, +3.1 mm per m/s.
    /// </summary>
    public static HullModel EznanCutter { get; } = FromFit(6, 45000f, draft: -0.434f, risePerSpeed: 0.0031f,
    [
        5.7379f, 0f, 0.6301f, 0.6283f, 0f, -1f, 0.4123f, 1f, 0f, 0.0275f, -0.0037f, 0.122f,
        0.515f, 5.817f, 1.009f, 0.78f, 0.964f, 0.199f, 0f, -0.0343f, 0.0072f, 0.0282f, 0.0003f
    ]);

    /// <summary>The fitted models by <c>ship_models.id</c>.</summary>
    public static IReadOnlyDictionary<uint, HullModel> Fitted { get; } = new Dictionary<uint, HullModel>
    {
        [3] = HarpoonClipper,
        [6] = EznanCutter
    };

    /// <summary>The model a hull of unknown ship model gets.</summary>
    public static HullModel Default => HarpoonClipper;

    /// <summary>
    /// The fit for a <c>ship_models</c> row, selected by ID or nearest mass. Impulses use the row's mass and mass box.
    /// A missing row uses <see cref="Default"/>.
    /// </summary>
    public static HullModel For(ShipModelV1? ship)
    {
        if (ship == null)
            return Default;
        var mass = float.IsFinite(ship.Mass) && ship.Mass > 0f ? ship.Mass : Default.Mass;
        var inertiaPerMass = float.IsFinite(ship.MassBoxSizeX) && float.IsFinite(ship.MassBoxSizeY) && ship.MassBoxSizeX > 0f && ship.MassBoxSizeY > 0f
            ? BoxInertiaPerMass(ship.MassBoxSizeX, ship.MassBoxSizeY)
            : Default.YawInertiaPerMass;
        if (!Fitted.TryGetValue(ship.Id, out var fitted))
        {
            fitted = Default;
            var distance = double.MaxValue;
            foreach (var model in Fitted.Values)
            {
                var candidate = Math.Abs(Math.Log(mass / model.Mass));
                if (candidate >= distance)
                    continue;
                fitted = model;
                distance = candidate;
            }
        }
        var centre = new Vector3(ship.MassCenterX, ship.MassCenterY, ship.MassCenterZ);
        return fitted with
        {
            Mass = mass,
            ReverseSpeedLimit = float.IsFinite(ship.ReverseVelocity) && ship.ReverseVelocity >= 0f ? ship.ReverseVelocity : fitted.ReverseSpeedLimit,
            YawInertiaPerMass = inertiaPerMass,
            PitchInertiaPerMass = BoxInertia(ship.MassBoxSizeY, ship.MassBoxSizeZ, Default.PitchInertiaPerMass),
            RollInertiaPerMass = BoxInertia(ship.MassBoxSizeX, ship.MassBoxSizeZ, Default.RollInertiaPerMass),
            MassCentre = HullBody.IsFinite(centre) ? centre : Vector3.Zero,
            WaterResistance = PositiveOrZero(ship.WaterResistance),
            WaterDamping = PositiveOrZero(ship.WaterDamping)
        };
    }

    private static double BoxInertia(float a, float b, double fallback) =>
        float.IsFinite(a) && float.IsFinite(b) && a > 0f && b > 0f ? Math.Max(0.001d, BoxInertiaPerMass(a, b)) : fallback;

    private static float PositiveOrZero(float value) => float.IsFinite(value) && value > 0f ? value : 0f;

    // r575 cryphysics_dedicate.dll, CBoxGeom mass properties (RVA 0x13f550): unit-density inertia divided by volume.
    private static double BoxInertiaPerMass(double halfA, double halfB) => (halfA * halfA + halfB * halfB) / 3d;

    /// <summary>
    /// A model from the fit's parameter vector, in the fit scripts' order: A, B, R, Du, Du2, Cwv, Dv, Cwu, Ys, Yr, Yu,
    /// Dw, Rup, Rdn, ks, vs, acc, tau, Dw2, Csv, Dv2, Dwu, Yu2 (side-session notes, slice7-hull-fit_model.py.txt).
    /// </summary>
    public static HullModel FromFit(uint shipModel, float mass, float draft, float risePerSpeed, float[] v)
    {
        if (v.Length != 23)
            throw new ArgumentException($"a hull fit has 23 parameters, not {v.Length}", nameof(v));
        return new HullModel
        {
            FittedShipModel = shipModel,
            Mass = mass,
            Draft = draft,
            RisePerSpeed = risePerSpeed,
            ThrustAtRest = v[0],
            ThrustFade = v[1],
            ReverseThrust = v[2],
            SurgeDrag = v[3],
            SurgeDrag2 = v[4],
            SurgeYawSway = v[5],
            SwayDrag = v[6],
            SwayYawSurge = v[7],
            YawSteer = v[8],
            YawSteerRpm = v[9],
            YawSteerSurge = v[10],
            YawDrag = v[11],
            RpmUp = v[12],
            RpmDown = v[13],
            SteerGain = v[14],
            SteerRate = v[15],
            SteerAccel = v[16],
            ThrottleLag = v[17],
            YawDrag2 = v[18],
            SwaySteerSurge = v[19],
            SwayDrag2 = v[20],
            YawDragSurge = v[21],
            YawSteerSurge2 = v[22]
        };
    }
}
