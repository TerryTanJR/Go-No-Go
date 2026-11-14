using UnityEngine;

// 用暫時的鏡頭從指定位置拍 Facility 場景（編輯模式），存成 PNG。
// unity command script run --file tools/room/Shots.cs --entry Shots.Take --args '["name", px,py,pz, tx,ty,tz, fov]'
public static class Shots
{
    public static string Take(string name, float px, float py, float pz, float tx, float ty, float tz, float fov)
    {
        var go = new GameObject("__ShotCam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.025f, 0.03f);
            var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            go.transform.position = new Vector3(px, py, pz);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(tx, ty, tz) - go.transform.position);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            var path = System.IO.Path.Combine(Application.dataPath, "..", "Screenshots", name + ".png");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            return path;
        }
        finally { Object.DestroyImmediate(go); }
    }
}
