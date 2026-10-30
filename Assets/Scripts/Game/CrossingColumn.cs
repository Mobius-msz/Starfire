using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 主力渡江：一队队红军战士从对岸走浮桥过河。
///
/// 只是气氛与目标计数，不参战——所有碰撞体都设成触发器，
/// 不挡子弹也不挡玩家，免得玩家隔着自己人打不到敌人。
/// 桥面右侧留给敌军进攻，队伍走左侧一半，一眼能看出"这是我们要保的人"。
/// </summary>
public class CrossingColumn : MonoBehaviour
{
    public const int Batches = 6;

    private const int PerBatch = 4;
    private const float WalkSpeed = 1.8f;
    private const float SpawnZ = 46f;
    private const float EndZ = 11f;

    private class Walker
    {
        public Transform Body;
        public Transform HipL;
        public Transform HipR;
        public Transform ShoulderL;
        public Transform ShoulderR;
        public float Phase;
        public float Speed;
        public float Sway;
    }

    private Transform levelRoot;
    private Action<int> onBatchCrossed;
    private readonly List<Walker> walkers = new List<Walker>();

    private int batch;
    private int crossed;
    private int reported;
    private float batchTimer;

    /// <summary>正在渡江的那一支。敌军按这个引用分出一部分火力打大部队。</summary>
    public static CrossingColumn Active { get; private set; }

    /// <summary>大部队撤离进度 0~1：已经过江的人 + 还在桥上行军的人。</summary>
    public float EvacuationProgress
    {
        get
        {
            int total = Batches * PerBatch;
            if (total <= 0) return 0f;

            float arrived = crossed;
            for (int i = 0; i < walkers.Count; i++)
            {
                if (walkers[i].Body == null) continue;
                float t = (SpawnZ - walkers[i].Body.position.z) / (SpawnZ - EndZ);
                arrived += Mathf.Clamp01(t);
            }

            return Mathf.Clamp01(arrived / total);
        }
    }

    /// <summary>离 from 最近的渡江战士，供敌军瞄准。没有目标时返回 false。</summary>
    public bool TryGetTarget(Vector3 from, float maxDistance, out Transform target)
    {
        target = null;
        float best = maxDistance;

        for (int i = 0; i < walkers.Count; i++)
        {
            if (walkers[i].Body == null) continue;

            float distance = Vector3.Distance(from, walkers[i].Body.position);
            if (distance >= best) continue;

            best = distance;
            target = walkers[i].Body;
        }

        return target != null;
    }

    public void Initialize(Transform root, Action<int> batchCallback)
    {
        levelRoot = root;
        onBatchCrossed = batchCallback;
    }

    public void Begin()
    {
        Stop();
        Active = this;
        batch = 0;
        crossed = 0;
        reported = 0;
        batchTimer = 2.5f;
    }

    public void Stop()
    {
        for (int i = 0; i < walkers.Count; i++)
        {
            if (walkers[i].Body != null) Destroy(walkers[i].Body.gameObject);
        }
        walkers.Clear();

        if (Active == this) Active = null;
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
    }

    private void Update()
    {
        if (batch < Batches)
        {
            batchTimer -= Time.deltaTime;
            if (batchTimer <= 0f)
            {
                SpawnBatch();
                batch++;
                batchTimer = 26f;
            }
        }

        float dt = Time.deltaTime;

        for (int i = walkers.Count - 1; i >= 0; i--)
        {
            Walker walker = walkers[i];
            if (walker.Body == null)
            {
                walkers.RemoveAt(i);
                continue;
            }

            Vector3 position = walker.Body.position;
            position.z -= walker.Speed * dt;
            position.x += Mathf.Sin(Time.time * 2.2f + walker.Sway) * 0.12f * dt;
            // 慢跑时的上下起伏
            position.y = 0.34f + Mathf.Abs(Mathf.Sin(walker.Phase)) * 0.055f;
            walker.Body.position = position;

            Animate(walker, dt);

            if (position.z < EndZ)
            {
                Destroy(walker.Body.gameObject);
                walkers.RemoveAt(i);

                crossed++;
                if (crossed / PerBatch > reported)
                {
                    reported = crossed / PerBatch;
                    if (onBatchCrossed != null) onBatchCrossed(reported);
                }
            }
        }
    }

    private void SpawnBatch()
    {
        if (levelRoot == null) return;

        for (int i = 0; i < PerBatch; i++)
        {
            GameObject go = new GameObject("CrossingSoldier");
            go.transform.SetParent(levelRoot, true);

            // 两列纵队走桥心：两侧是沙袋胸墙，中间这条通道留给定大部队
            float x = (i % 2 == 0 ? -0.72f : 0.72f)
                      + UnityEngine.Random.Range(-0.12f, 0.12f);
            float z = SpawnZ + i * 1.9f + UnityEngine.Random.Range(-0.25f, 0.25f);

            go.transform.position = new Vector3(x, 0.34f, z);
            // 行进时身体略微前倾，像小跑而不是散步
            go.transform.rotation = Quaternion.Euler(-4f, 180f, 0f);

            EnemySpawner.BuildBody(go.transform, true);

            // 纯气氛角色：不挡枪、不挡路
            foreach (Collider c in go.GetComponentsInChildren<Collider>()) c.isTrigger = true;

            walkers.Add(new Walker
            {
                Body = go.transform,
                HipL = go.transform.Find("HipL"),
                HipR = go.transform.Find("HipR"),
                ShoulderL = go.transform.Find("ShoulderL"),
                ShoulderR = go.transform.Find("ShoulderR"),
                Phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                Speed = WalkSpeed + UnityEngine.Random.Range(-0.18f, 0.22f),
                Sway = UnityEngine.Random.Range(0f, 10f)
            });
        }
    }

    private static void Animate(Walker walker, float dt)
    {
        walker.Phase += dt * 10.2f;

        // 慢跑：腿摆幅更大、手臂主动前后摆，脚下带一点弹跳
        float swing = Mathf.Sin(walker.Phase) * 36f;
        float arm = -swing * 0.62f;

        if (walker.HipL != null) walker.HipL.localRotation = Quaternion.Euler(swing + 3f, 0f, 0f);
        if (walker.HipR != null) walker.HipR.localRotation = Quaternion.Euler(-swing + 3f, 0f, 0f);
        if (walker.ShoulderL != null) walker.ShoulderL.localRotation = Quaternion.Euler(arm, 0f, 0f);
        if (walker.ShoulderR != null) walker.ShoulderR.localRotation = Quaternion.Euler(-arm, 0f, 0f);
    }
}
