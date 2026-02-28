using UnityEngine;

public class DamageManager : MonoBehaviour
{
    [Header("Collision FX")]
    [SerializeField] private ParticleSystem collisionParticlePrefab;
    [SerializeField] private bool alignToCollisionNormal = true;
    [SerializeField, Min(0.1f)] private float fallbackDestroyDelay = 6.0f;

    public void SpawnCollisionEffect(Collision collision)
    {
        if (collisionParticlePrefab == null || collision == null || collision.contactCount == 0)
            return;

        ContactPoint contact = collision.GetContact(0);
        SpawnCollisionEffect(contact.point, contact.normal);
    }

    public void SpawnCollisionEffect(Vector3 worldPoint, Vector3 worldNormal)
    {
        if (collisionParticlePrefab == null)
            return;

        Quaternion rotation = Quaternion.identity;
        if (alignToCollisionNormal && worldNormal.sqrMagnitude > 0.0001f)
            rotation = Quaternion.LookRotation(worldNormal.normalized, Vector3.up);

        ParticleSystem instance = Instantiate(collisionParticlePrefab, worldPoint, rotation);
        instance.Play(true);

        float destroyDelay = Mathf.Max(0.1f, EstimateLifetime(instance));
        Destroy(instance.gameObject, destroyDelay);
    }

    private float EstimateLifetime(ParticleSystem root)
    {
        if (root == null)
            return fallbackDestroyDelay;

        float maxLifetime = 0.0f;
        ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem ps = systems[i];
            ParticleSystem.MainModule main = ps.main;
            float life = main.duration + main.startLifetime.constantMax;
            if (main.loop)
                life = fallbackDestroyDelay;
            maxLifetime = Mathf.Max(maxLifetime, life);
        }

        return maxLifetime > 0.0f ? maxLifetime : fallbackDestroyDelay;
    }
}
