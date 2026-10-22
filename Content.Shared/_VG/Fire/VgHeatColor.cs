namespace Content.Shared._VG.Fire;

/// <summary>
///     vg13 heat2color() (code/__HELPERS/type2type.dm): the colour something glows at a temperature, roughly black-body.
/// </summary>
public static class VgHeatColor
{
    public static Color FromTemperature(float kelvin)
    {
        var t = kelvin / 100;

        var r = t <= 66 ? 255 : Clamp(329.698727446 * Math.Pow(t - 60, -0.1332047592));
        var g = t <= 66
            ? Clamp(99.4708025861 * Math.Log(t) - 161.1195681661)
            : Clamp(288.1221695283 * Math.Pow(t - 60, -0.0755148492));
        var b = t >= 66 ? 255 : t <= 16 ? 0 : Clamp(138.5177312231 * Math.Log(t - 10) - 305.0447927307);

        return new Color((byte) r, (byte) g, (byte) b);
    }

    private static double Clamp(double value)
    {
        // BYOND's rgb() rounds; NaN (log of a non-positive number) counts as 0.
        return double.IsNaN(value) ? 0 : Math.Round(Math.Clamp(value, 0, 255));
    }
}
