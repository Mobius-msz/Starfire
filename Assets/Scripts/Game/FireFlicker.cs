using UnityEngine;

/// <summary>火光闪烁：用 Perlin 噪声让点光源强度与火苗大小轻微起伏。</summary>
public class FireFlicker : MonoBehaviour
{
    [Tooltip("可选：火苗 Transform，会随噪声缩放")]
    public Transform flame;

    private Light source;
    private float baseIntensity;

    private Vector3 baseFlameScale;
    private bool hasFlameScale;

    private void Awake()
    {
        source = GetComponent<Light>();
        if (source != null) baseIntensity = source.intensity;
    }

    private void Update()
    {
        float noise = Mathf.PerlinNoise(Time.time * 7f, 0.5f);

        if (source != null)
        {
            source.intensity = baseIntensity * (0.78f + noise * 0.5f);
        }

        if (flame != null)
        {
            if (!hasFlameScale)
            {
                baseFlameScale = flame.localScale;
                hasFlameScale = true;
            }

            flame.localScale = baseFlameScale * (0.9f + noise * 0.25f);
        }
    }
}
