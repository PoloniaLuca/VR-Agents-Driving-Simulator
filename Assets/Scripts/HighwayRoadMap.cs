using System;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// Mappa stradale campionata da una spline. Serve a due cose:
///  1) convertire una posizione mondo in (distanza lungo la strada, offset laterale in metri)
///     -> DataLogger: distance_to_gantry e lane_offset
///  2) trovare posizione/direzione a una certa distanza dall'inizio della spline
///     -> HighwayTriggerGenerator: piazzare i trigger
///
/// La distanza e' la lunghezza d'arco misurata dall'inizio della spline (0 = inizio, ~5000 = fine).
/// Il segno dell'offset laterale usa la stessa convenzione di ExperimentManager.GetPoseOnSpline:
/// normale = Cross(Vector3.up, tangente). Positivo = lato verso cui punta laneOffset positivo.
/// </summary>
public class HighwayRoadMap
{
    private readonly Vector3[] pos;   // posizioni mondo dei campioni
    private readonly float[] cum;     // lunghezza d'arco cumulata (m)
    private int hint = -1;            // ultimo indice trovato (ricerca locale veloce a 60 Hz)

    private const int HintWindow = 200;       // campioni cercati attorno all'ultimo indice
    private const float HintFallbackM = 100f; // se il match locale e' piu' lontano, ricerca completa

    public float Length => cum[cum.Length - 1];

    public HighwayRoadMap(SplineContainer container, int splineIndex, float spacingMetres = 0.5f)
    {
        if (container == null)
            throw new ArgumentNullException(nameof(container));

        Spline spline = container.Splines[splineIndex];
        int n = Mathf.Clamp(
            Mathf.CeilToInt(spline.GetLength() / Mathf.Max(0.1f, spacingMetres)), 64, 60000);

        pos = new Vector3[n + 1];
        cum = new float[n + 1];

        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            float3 localPos = SplineUtility.EvaluatePosition(spline, t);
            pos[i] = container.transform.TransformPoint((Vector3)localPos);
            if (i > 0)
                cum[i] = cum[i - 1] + Vector3.Distance(pos[i - 1], pos[i]);
        }
    }

    /// <summary>Da chiamare dopo un teletrasporto del veicolo (inizio trial).</summary>
    public void ResetHint() { hint = -1; }

    /// <summary>Vettore laterale (stessa convenzione di Cross(up, forward)) per una direzione orizzontale.</summary>
    public static Vector3 SideVector(Vector3 forward)
    {
        return new Vector3(forward.z, 0f, -forward.x);
    }

    // ---------------------------------------------------------------
    //  POSIZIONE MONDO -> (distanza, laterale)
    // ---------------------------------------------------------------
    public bool Project(Vector3 world, out float distance, out float side)
    {
        distance = 0f;
        side = 0f;

        int last = pos.Length - 1;
        int lo = 0, hi = last;
        if (hint >= 0)
        {
            lo = Mathf.Max(0, hint - HintWindow);
            hi = Mathf.Min(last, hint + HintWindow);
        }

        int best = NearestIndex(world, lo, hi, out float bestSq);
        if (hint >= 0 && bestSq > HintFallbackM * HintFallbackM)
            best = NearestIndex(world, 0, last, out bestSq);

        hint = best;

        int bestSeg = -1;
        float bestU = 0f;
        float bestD2 = float.MaxValue;

        for (int s = best - 1; s <= best; s++)
        {
            if (s < 0 || s >= last) continue;
            ProjectOnSegment(world, s, out float u, out float d2);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                bestSeg = s;
                bestU = u;
            }
        }

        if (bestSeg < 0) return false;

        distance = cum[bestSeg] + bestU * (cum[bestSeg + 1] - cum[bestSeg]);

        Vector3 fwd = SegmentForward(bestSeg);
        Vector3 closest = Vector3.Lerp(pos[bestSeg], pos[bestSeg + 1], bestU);
        Vector3 off = world - closest;
        side = off.x * fwd.z - off.z * fwd.x; // dot(off, Cross(up, fwd))
        return true;
    }

    // ---------------------------------------------------------------
    //  DISTANZA -> POSIZIONE MONDO
    // ---------------------------------------------------------------
    public void GetPoint(float distance, out Vector3 point, out Vector3 forward)
    {
        float d = Mathf.Clamp(distance, 0f, Length);
        int s = SegmentIndexAt(d);
        float segLen = cum[s + 1] - cum[s];
        float u = segLen > 1e-5f ? (d - cum[s]) / segLen : 0f;
        point = Vector3.Lerp(pos[s], pos[s + 1], u);
        forward = SegmentForward(s);
    }

    public float HeadingDegAt(float distance)
    {
        Vector3 f = SegmentForward(SegmentIndexAt(Mathf.Clamp(distance, 0f, Length)));
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    /// <summary>True se la strada e' rettilinea attorno a 'distance' (variazione di direzione trascurabile).</summary>
    public bool IsStraight(float distance, float halfWindowMetres, float maxHeadingChangeDeg)
    {
        float a = HeadingDegAt(distance - halfWindowMetres);
        float m = HeadingDegAt(distance);
        float b = HeadingDegAt(distance + halfWindowMetres);
        return Mathf.Abs(Mathf.DeltaAngle(a, m)) <= maxHeadingChangeDeg
            && Mathf.Abs(Mathf.DeltaAngle(m, b)) <= maxHeadingChangeDeg;
    }

    // ---------------------------------------------------------------
    //  INTERNI
    // ---------------------------------------------------------------
    private int SegmentIndexAt(float distance)
    {
        int idx = Array.BinarySearch(cum, distance);
        if (idx < 0) idx = ~idx - 1;
        return Mathf.Clamp(idx, 0, cum.Length - 2);
    }

    private Vector3 SegmentForward(int s)
    {
        Vector3 d = pos[s + 1] - pos[s];
        d.y = 0f;
        return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
    }

    private int NearestIndex(Vector3 world, int lo, int hi, out float bestSq)
    {
        int best = lo;
        bestSq = float.MaxValue;
        for (int i = lo; i <= hi; i++)
        {
            float dx = pos[i].x - world.x;
            float dz = pos[i].z - world.z;
            float d2 = dx * dx + dz * dz;
            if (d2 < bestSq)
            {
                bestSq = d2;
                best = i;
            }
        }
        return best;
    }

    private void ProjectOnSegment(Vector3 world, int s, out float u, out float d2)
    {
        Vector3 a = pos[s];
        Vector3 b = pos[s + 1];
        float abx = b.x - a.x, abz = b.z - a.z;
        float apx = world.x - a.x, apz = world.z - a.z;
        float ab2 = abx * abx + abz * abz;
        u = ab2 > 1e-8f ? Mathf.Clamp01((apx * abx + apz * abz) / ab2) : 0f;
        float cx = a.x + abx * u - world.x;
        float cz = a.z + abz * u - world.z;
        d2 = cx * cx + cz * cz;
    }
}
