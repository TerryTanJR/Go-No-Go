using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 經緯度 ↔ 行星物件空間。和 Planet.shader 的 LatLonUV 用同一套定義：
    /// 經度從 +X 往 +Z 增加（從北極往下看是逆時針，也就是往東），+Y 是北極。
    /// </summary>
    public static class GeoMath
    {
        public static Vector3 LatLonToLocal(float latDeg, float lonDeg, float radius = 1f)
        {
            float lat = latDeg * Mathf.Deg2Rad, lon = lonDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon)) * radius;
        }
    }
}
