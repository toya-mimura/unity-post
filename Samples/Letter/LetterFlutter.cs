using System.Collections;
using UnityEngine;

/// <summary>
/// 手紙（板ポリ）が風に乗ってふわふわ漂いながら、地点Aから「だいたい地点B」へ落ちていく動き。
/// 物理演算は使わず、経路＋揺れ＋ノイズを合成して動かす（着地点を確実にコントロールできる）。
/// 使い方：空のGameObjectにこのスクリプトを付け、letter.fbx をその子にする。
/// </summary>
public class LetterFlutter : MonoBehaviour
{
    [Header("経路 / Path")]
    public Transform pointA;
    public Transform pointB;
    [Tooltip("B を中心に、この半径の円内のどこかに着地する")]
    public float landingRadius = 1.0f;
    [Tooltip("A→B にかかる秒数")]
    public float duration = 6f;
    [Tooltip("所要時間のランダム幅（±秒）")]
    public float durationJitter = 1.0f;
    [Tooltip("出だしで風にふわっと持ち上げられる高さ")]
    public float liftHeight = 0.5f;
    public bool playOnStart = true;

    [Header("左右の揺れ（木の葉が振り子みたいに揺れる）/ Sway")]
    public float swayAmplitude = 0.8f;
    [Tooltip("1秒あたりの往復回数")]
    public float swayFrequency = 0.5f;
    [Tooltip("揺れの端でふっと浮き上がる量")]
    public float bobAmplitude = 0.2f;

    [Header("風のゆらぎ（不規則さ）/ Noise")]
    public float noiseAmplitude = 0.4f;
    public float noiseFrequency = 0.35f;

    [Header("回転 / Rotation")]
    [Tooltip("揺れに合わせて傾く最大角度")]
    public float maxTilt = 35f;
    [Tooltip("ゆっくり水平回転する速さ（度/秒、符号はランダム）")]
    public float spinSpeed = 45f;
    [Tooltip("ノイズによるパタパタ感（度）")]
    public float wobbleAngle = 15f;

    [Header("高さの落ち方（横軸0→1 = 経過、縦軸 = Aの高さに対する割合）")]
    public AnimationCurve heightCurve = new AnimationCurve(
        new Keyframe(0f, 1f, 0f, 0f),
        new Keyframe(1f, 0f, -1.5f, 0f)); // 最初ゆっくり、後半ストンと落ちすぎない程度

    Coroutine _running;

    void Start()
    {
        if (playOnStart) Play();
    }

    /// <summary>外部（ボタン・トリガー等）から呼べば何度でも再生できる</summary>
    public void Play()
    {
        if (_running != null) StopCoroutine(_running);
        _running = StartCoroutine(FlyRoutine());
    }

    IEnumerator FlyRoutine()
    {
        Vector3 start = pointA.position;
        Vector2 offset = Random.insideUnitCircle * landingRadius;
        Vector3 end = pointB.position + new Vector3(offset.x, 0f, offset.y);

        float dur = Mathf.Max(0.5f, duration + Random.Range(-durationJitter, durationJitter));
        float phase = Random.Range(0f, Mathf.PI * 2f);
        float seed = Random.Range(0f, 1000f);
        float spinDir = Random.value < 0.5f ? -1f : 1f;
        float freqJitter = Random.Range(0.8f, 1.2f);
        float finalYaw = Random.Range(0f, 360f);

        // 進行方向（水平）と、それに直交する「横揺れ方向」
        Vector3 flat = end - start; flat.y = 0f;
        Vector3 dir = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, dir);

        float yaw = transform.eulerAngles.y;
        float elapsed = 0f;

        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float env = Mathf.Sin(Mathf.PI * t);          // 始点・終点で揺れを0にする包絡線
            float theta = phase + elapsed * swayFrequency * freqJitter * Mathf.PI * 2f;

            // ---- 位置 ----
            Vector3 horiz = Vector3.Lerp(start, end, t);
            float y = Mathf.Lerp(end.y, start.y, heightCurve.Evaluate(t))
                      + liftHeight * Mathf.Sin(Mathf.PI * Mathf.Min(t * 2f, 1f)) * (1f - t);

            float sway = Mathf.Sin(theta) * swayAmplitude;
            float bob = -Mathf.Cos(theta * 2f) * bobAmplitude;   // 揺れの端で浮く
            float n = elapsed * noiseFrequency;
            Vector3 noise = new Vector3(
                Mathf.PerlinNoise(seed, n) - 0.5f,
                Mathf.PerlinNoise(seed + 31.7f, n) - 0.5f,
                Mathf.PerlinNoise(seed + 77.3f, n) - 0.5f) * 2f * noiseAmplitude;

            transform.position = new Vector3(horiz.x, y, horiz.z)
                                 + (side * sway + Vector3.up * bob + noise) * env;

            // ---- 回転 ----
            yaw += spinSpeed * spinDir * Time.deltaTime * env;
            float roll = Mathf.Cos(theta) * maxTilt;               // 揺れの速度に合わせて傾く
            float pitch = (Mathf.PerlinNoise(seed + 13.1f, n * 2f) - 0.5f) * 2f * wobbleAngle;
            Quaternion flying = Quaternion.AngleAxis(yaw, Vector3.up)
                                * Quaternion.Euler(pitch * env, 0f, roll * env);

            // 最後の15%で、地面にぺたっと寝かせる
            float settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, t));
            Quaternion landed = Quaternion.Euler(0f, Mathf.LerpAngle(yaw, finalYaw, 0.3f), 0f);
            transform.rotation = Quaternion.Slerp(flying, landed, settle);

            yield return null;
        }

        transform.position = end;
        _running = null;
    }

    // シーンビューで着地範囲を表示
    void OnDrawGizmos()
    {
        if (pointB == null) return;
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
        const int seg = 32;
        Vector3 c = pointB.position;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
            Gizmos.DrawLine(c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * landingRadius,
                            c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * landingRadius);
        }
        if (pointA != null) { Gizmos.color = Color.cyan; Gizmos.DrawLine(pointA.position, c); }
    }
}
