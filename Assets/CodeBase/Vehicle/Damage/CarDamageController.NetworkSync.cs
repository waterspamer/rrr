using System;
using UnityEngine;

public sealed class CarDamageNetworkSnapshot
{
    public int revision;
    public int width;
    public int height;
    public byte[] rawBytes;
    public bool hasImpactPoint;
    public Vector3 worldPoint;
    public bool hasImpactNormal;
    public Vector3 worldNormal;
}

public sealed class NetworkVehicleCollisionReport
{
    public string otherPlayerId;
    public Vector3 worldPoint;
    public Vector3 worldNormal;
    public Vector3 relativeVelocity;
    public float impulseMagnitude;
}

public partial class CarDamageController
{
    private int damageRevision;

    public event Action<CarDamageNetworkSnapshot> DamageMapChanged;
    public event Action<NetworkVehicleCollisionReport> NetworkVehicleCollisionDetected;

    public int DamageRevision => damageRevision;

    public void EnsureNetworkTextureReady()
    {
        if (cpuTexture != null && runtimeTexture != null)
            return;

        if (!isInitialized)
            InitializeFromSources(GetComponentsInChildren<Collider>(true), GetComponentsInChildren<Renderer>(true));
        else if (cpuTexture == null || runtimeTexture == null)
            CreateTexture();
    }

    public bool TryCaptureDamageSnapshot(out CarDamageNetworkSnapshot snapshot)
    {
        EnsureNetworkTextureReady();
        if (cpuTexture == null)
        {
            snapshot = null;
            return false;
        }

        byte[] bytes = cpuTexture.GetRawTextureData();
        snapshot = new CarDamageNetworkSnapshot
        {
            revision = damageRevision,
            width = textureWidth,
            height = textureHeight,
            rawBytes = bytes != null ? (byte[])bytes.Clone() : Array.Empty<byte>(),
        };
        return true;
    }

    public void ApplyNetworkDamageSnapshot(CarDamageNetworkSnapshot snapshot, DamageManager managerOverride = null)
    {
        if (snapshot == null || snapshot.rawBytes == null)
            return;

        EnsureNetworkTextureReady();
        if (cpuTexture == null || runtimeTexture == null)
            return;

        if (textureWidth != snapshot.width || textureHeight != snapshot.height)
        {
            textureWidth = Mathf.Max(1, snapshot.width);
            textureHeight = Mathf.Max(1, snapshot.height);
            ResetInitializationState();
            EnsureNetworkTextureReady();
        }

        cpuTexture.LoadRawTextureData(snapshot.rawBytes);
        cpuTexture.Apply(false, false);
        Graphics.Blit(cpuTexture, runtimeTexture);
        ApplyRuntimeTextureToTargets();

        if (snapshot.revision > damageRevision)
            damageRevision = snapshot.revision;

        if (snapshot.hasImpactPoint)
        {
            DamageManager manager = managerOverride != null ? managerOverride : damageManager;
            manager?.SpawnCollisionEffect(snapshot.worldPoint, snapshot.hasImpactNormal ? snapshot.worldNormal : Vector3.up);
        }
    }

    private void NotifyDamageMapChanged()
    {
        NotifyDamageMapChangedInternal(false, Vector3.zero, false, Vector3.up);
    }

    private void NotifyDamageMapChanged(Vector3 worldPoint, Vector3 worldNormal)
    {
        NotifyDamageMapChangedInternal(true, worldPoint, true, worldNormal);
    }

    private void NotifyDamageMapChangedInternal(bool hasPoint, Vector3 worldPoint, bool hasNormal, Vector3 worldNormal)
    {
        if (DamageMapChanged == null)
            return;

        if (!TryCaptureDamageSnapshot(out CarDamageNetworkSnapshot snapshot))
            return;

        damageRevision = Mathf.Max(1, damageRevision + 1);
        snapshot.revision = damageRevision;
        snapshot.hasImpactPoint = hasPoint;
        snapshot.worldPoint = worldPoint;
        snapshot.hasImpactNormal = hasNormal;
        snapshot.worldNormal = worldNormal;
        DamageMapChanged?.Invoke(snapshot);
    }

    private void NotifyNetworkVehicleCollision(Collision collision, string otherPlayerId)
    {
        if (NetworkVehicleCollisionDetected == null || collision == null || string.IsNullOrWhiteSpace(otherPlayerId))
            return;

        ContactPoint[] contacts = collision.contacts;
        Vector3 worldPoint = contacts != null && contacts.Length > 0 ? contacts[0].point : transform.position;
        Vector3 worldNormal = contacts != null && contacts.Length > 0 ? contacts[0].normal : Vector3.up;

        NetworkVehicleCollisionDetected.Invoke(new NetworkVehicleCollisionReport
        {
            otherPlayerId = otherPlayerId,
            worldPoint = worldPoint,
            worldNormal = worldNormal,
            relativeVelocity = collision.relativeVelocity,
            impulseMagnitude = collision.impulse.magnitude
        });
    }
}
