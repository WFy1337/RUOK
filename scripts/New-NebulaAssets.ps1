param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$references = @([System.Drawing.Bitmap].Assembly.Location, [System.Drawing.Color].Assembly.Location,
    [System.Runtime.InteropServices.Marshal].Assembly.Location)
$references += [System.Drawing.Bitmap].Assembly.GetReferencedAssemblies() |
    Where-Object Name -Like 'System.Private.Windows.*' |
    ForEach-Object { [System.Reflection.Assembly]::Load($_).Location }
Add-Type -ReferencedAssemblies ($references | Select-Object -Unique) -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class RuokNebulaAssets
{
    private static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144269);
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) / (double)uint.MaxValue;
        }
    }

    private static double Noise(double x, double y, int seed)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double u = x - ix, v = y - iy;
        u = u * u * (3 - 2 * u);
        v = v * v * (3 - 2 * v);
        double a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed);
        double c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
        return (a + (b - a) * u) * (1 - v) + (c + (d - c) * u) * v;
    }

    private static double Fractal(double x, double y, int seed)
    {
        double value = 0, amplitude = .55;
        for (int octave = 0; octave < 5; octave++)
        {
            value += Noise(x, y, seed + octave * 7) * amplitude;
            x = x * 2.03 + 7.1;
            y = y * 2.03 - 2.7;
            amplitude *= .49;
        }
        return value;
    }

    public static void Write(string path, int seed)
    {
        const int size = 640;
        using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            var pixels = new byte[size * size * 4];
            for (int row = 0; row < size; row++)
            for (int col = 0; col < size; col++)
            {
                double x = (col + .5) / size * 2 - 1, y = (row + .5) / size * 2 - 1;
                double wx = x + .34 * (Fractal(x * 2.3 + 9, y * 2.3, seed) - .5);
                double wy = y + .38 * (Fractal(x * 2.5, y * 2.5 + 13, seed + 5) - .5);
                double curve = wy + .29 * Math.Sin(wx * 3.8 + seed * .04);
                double ribbon = Math.Exp(-curve * curve / .046);
                double veil = Math.Exp(-Math.Pow(wx * .7 - wy + .12, 2) / .16) * .35;
                double dust = Fractal(wx * 8.5 + 2, wy * 8.5 - 5, seed);
                double filaments = Fractal(wx * 22, wy * 22, seed + 17);
                double edge = Math.Max(0, Math.Min(1, (1.13 - Math.Sqrt(x * x + y * y)) / .3));
                edge = edge * edge * (3 - 2 * edge);
                double alpha = Math.Min(1, (ribbon + veil) * (.12 + 1.6 * dust * dust + .25 * filaments) * edge);
                int index = (row * size + col) * 4;
                pixels[index] = pixels[index + 1] = pixels[index + 2] = 255;
                pixels[index + 3] = (byte)Math.Round(alpha * 255);
            }
            var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
            finally { bitmap.UnlockBits(data); }
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
'@
$destination = Join-Path $PSScriptRoot '..\src\RUOK.App\Assets\Nebula'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
[RuokNebulaAssets]::Write((Join-Path $destination 'DustA.png'), 11)
[RuokNebulaAssets]::Write((Join-Path $destination 'DustB.png'), 53)
'Generated two original 640px nebula dust masks; no external imagery was used.'
