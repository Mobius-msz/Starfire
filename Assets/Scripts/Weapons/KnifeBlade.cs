using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 大刀的刀身：一块梯形棱柱。刀背一边竖直，刃口在刀尖处斜收，
/// 顶边平齐——不再用几个方块叠出一个缺了角的阶梯刀尖。
/// </summary>
public static class KnifeBlade
{
    /// <summary>
    /// 四个角按刀身平面逆时针给：左下、右下、右上、左上（父节点局部坐标，单位米）。
    /// </summary>
    public static GameObject Create(string name, Transform parent,
                                    Vector2 bottomLeft, Vector2 bottomRight,
                                    Vector2 topRight, Vector2 topLeft,
                                    float thickness, Color color, bool castShadow)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        float half = thickness * 0.5f;

        Vector3[] corners =
        {
            new Vector3(bottomLeft.x, bottomLeft.y, 0f),
            new Vector3(bottomRight.x, bottomRight.y, 0f),
            new Vector3(topRight.x, topRight.y, 0f),
            new Vector3(topLeft.x, topLeft.y, 0f)
        };

        List<Vector3> verts = new List<Vector3>(48);
        List<Vector3> norms = new List<Vector3>(48);
        List<int> tris = new List<int>(24);

        // 正反两面
        for (int side = 0; side < 2; side++)
        {
            float z = side == 0 ? -half : half;
            Vector3 normal = new Vector3(0f, 0f, side == 0 ? -1f : 1f);
            AddQuad(verts, norms, tris,
                corners[0] + Vector3.forward * z, corners[1] + Vector3.forward * z,
                corners[2] + Vector3.forward * z, corners[3] + Vector3.forward * z, normal);
        }

        // 四条侧棱：倒角面也要有，不然薄板侧看是一条边
        for (int i = 0; i < 4; i++)
        {
            Vector3 p0 = corners[i];
            Vector3 p1 = corners[(i + 1) % 4];
            Vector3 e = p1 - p0;
            Vector3 normal = new Vector3(e.y, -e.x, 0f).normalized;

            AddQuad(verts, norms, tris,
                p0 - Vector3.forward * half, p1 - Vector3.forward * half,
                p1 + Vector3.forward * half, p0 + Vector3.forward * half, normal);
        }

        Mesh mesh = new Mesh();
        mesh.vertices = verts.ToArray();
        mesh.normals = norms.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();

        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = LevelBuilder.MakeMaterial(color);
        renderer.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.receiveShadows = castShadow;

        return go;
    }

    /// <summary>
    /// 每个面正反两种绕序各写一遍：两面都看得见，
    /// 法线是显式给的，不会被重合的面平均成零。
    /// </summary>
    private static void AddQuad(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                                Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        AddTriangle(verts, norms, tris, a, b, c, normal);
        AddTriangle(verts, norms, tris, a, c, d, normal);
        AddTriangle(verts, norms, tris, a, c, b, normal);
        AddTriangle(verts, norms, tris, a, d, c, normal);
    }

    private static void AddTriangle(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                                    Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
    {
        int index = verts.Count;
        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        norms.Add(normal);
        norms.Add(normal);
        norms.Add(normal);
        tris.Add(index);
        tris.Add(index + 1);
        tris.Add(index + 2);
    }
}
