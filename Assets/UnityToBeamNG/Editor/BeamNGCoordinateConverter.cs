using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>
    /// Unity: left-handed, Y up, Z forward. BeamNG: right-handed, Z up, Y forward (north).
    /// BeamNG(x, y, z) = Unity(x, z, y).
    /// Rotation: reflection P (Y/Z swap) gives B = P R P. As a quaternion the rotation axis is
    /// a pseudovector, so it is negated and swapped: (x,y,z,w) -> (-x,-z,-y,w).
    /// RotationMatrix returns the matrix in the order BeamNG expects in items.level.json.
    /// A first in-game test (BeamNG 0.40) showed that the plain row-major layout rotated objects the
    /// wrong way around, so the transposed layout is used. To be confirmed by a second test.
    /// </summary>
    public static class BeamNGCoordinateConverter
    {
        public static Vector3 Position(Vector3 u) { return new Vector3(u.x, u.z, u.y); }
        public static Vector3 Direction(Vector3 u) { return new Vector3(u.x, u.z, u.y); }
        public static Vector3 Scale(Vector3 s) { return new Vector3(s.x, s.z, s.y); }

        /// <summary>Unity rotation -> BeamNG rotation (quaternion in BeamNG axes).</summary>
        public static Quaternion Rotation(Quaternion q) { return new Quaternion(-q.x, -q.z, -q.y, q.w); }

        /// <summary>BeamNG quaternion -> 9 values for "rotationMatrix" (transposed standard matrix).</summary>
        public static float[] RotationMatrix(Quaternion q)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            float n = x * x + y * y + z * z + w * w;
            if (n < 1e-12f) return new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            float s = 2f / n;
            float xx = x * x * s, yy = y * y * s, zz = z * z * s;
            float xy = x * y * s, xz = x * z * s, yz = y * z * s;
            float wx = w * x * s, wy = w * y * s, wz = w * z * s;
            // standard matrix R = [[1-(yy+zz), xy-wz, xz+wy], [xy+wz, 1-(xx+zz), yz-wx], [xz-wy, yz+wx, 1-(xx+yy)]]
            // written out transposed (R^T):
            return new float[]
            {
                1f - (yy + zz), xy + wz,        xz - wy,
                xy - wz,        1f - (xx + zz), yz + wx,
                xz + wy,        yz - wx,        1f - (xx + yy)
            };
        }
    }
}
