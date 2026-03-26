using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed partial class ArcadePrototypeCarController
{
    private enum GearShiftState
    {
        Ready,
        Shifting
    }

    [System.Serializable]
    public struct SimulationState
    {
        public int currentGear;
        public int requestedGear;
        public float currentRpm;
        public float shiftTimer;
        public float shiftTargetRpm;
        public int shiftState;
        public float currentSteerAngle;
        public float currentDriftKickForce;
        public float currentSteeringWheelAngle;
        public float nitroAmount;
        public bool nitroActive;
        public bool nitroInitialized;
    }

    private void ApplyBodySetup()
    {
        if (body == null)
            return;

        if (handlingConfig != null)
            body.mass = Mathf.Max(50.0f, handlingConfig.mass);
        RefreshBodyColliderShape();
        float centerOfMassY = suspensionConfig != null ? suspensionConfig.centerOfMassHeight : 0.3f;
        centerOfMassY = Mathf.Max(0.05f, centerOfMassY + centerOfMassOffsetY + GetWheelRadius());
        body.centerOfMass = new Vector3(0.0f, centerOfMassY, 0.0f);

        body.interpolation = RigidbodyInterpolation.None;
        body.maxAngularVelocity = maxAngularVelocity;
        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = true;
        body.constraints = RigidbodyConstraints.None;
        SyncCustomBodyState(false);
    }

    private void ResetSimulationState()
    {
        EnsureDefaultGears();
        simulationState.currentGear = 1;
        simulationState.requestedGear = 1;
        simulationState.currentRpm = engineConfig != null && engineConfig.engine != null ? engineConfig.engine.idleRpm : 900.0f;
        simulationState.shiftTimer = 0.0f;
        simulationState.shiftTargetRpm = simulationState.currentRpm;
        simulationState.shiftState = (int)GearShiftState.Ready;
        simulationState.currentSteerAngle = 0.0f;
        simulationState.currentDriftKickForce = 0.0f;
        simulationState.currentSteeringWheelAngle = 0.0f;
        simulationState.nitroAmount = handlingConfig != null ? Mathf.Clamp01(handlingConfig.nitroStart) : 1.0f;
        simulationState.nitroActive = false;
        simulationState.nitroInitialized = true;

        for (int i = 0; i < wheelStates.Length; i++)
        {
            wheelStates[i].grounded = false;
            wheelStates[i].suspensionLength = GetSuspensionRestLength();
            wheelStates[i].compression = 0.0f;
            wheelStates[i].compression01 = 0.0f;
            wheelStates[i].spinAngle = 0.0f;
            wheelStates[i].spinVelocity = 0.0f;
            wheelStates[i].steerAngle = 0.0f;
            wheelStates[i].springForce = 0.0f;
            wheelStates[i].contactPoint = transform.position;
            wheelStates[i].contactNormal = Vector3.up;
        }

        groundedWheels = 0;
        airTime = 0.0f;
        timeSinceGrounded = 0.0f;
        landingGripBlend = 1.0f;
        wasGroundedLastFrame = false;
        SyncCustomBodyState(true);
    }

    private VehicleInput SanitizeInput(VehicleInput input)
    {
        input.steer = Mathf.Clamp(input.steer, -1.0f, 1.0f);
        input.throttle = Mathf.Clamp(input.throttle, -1.0f, 1.0f);
        input.brake = Mathf.Clamp01(input.brake);
        input.handbrake = Mathf.Clamp01(input.handbrake);
        return input;
    }

    private void ProbeWheels(float deltaTime)
    {
        groundedWheels = 0;
        float suspensionDistance = GetSuspensionDistance();
        float wheelRadius = GetWheelRadius();
        float restLength = GetSuspensionRestLength();
        Vector3 up = transform.up;

        for (int i = 0; i < wheelBindings.Length; i++)
        {
            WheelBinding binding = wheelBindings[i];
            if (binding.hardpoint == null)
                continue;

            WheelRuntimeState state = wheelStates[i];
            state.steerAngle = binding.steer ? simulationState.currentSteerAngle : 0.0f;
            state.grounded = false;
            state.compression = 0.0f;
            state.compression01 = 0.0f;
            state.springForce = 0.0f;
            state.contactNormal = up;
            state.contactPoint = binding.hardpoint.position - up * (restLength + wheelRadius);
            state.suspensionLength = suspensionDistance;

            if (TryGetGroundHit(binding.hardpoint.position, up, suspensionDistance, wheelRadius, out RaycastHit hit, out float springLength))
            {
                state.grounded = true;
                state.contactPoint = hit.point;
                state.contactNormal = hit.normal;
                state.suspensionLength = springLength;
                state.compression = Mathf.Clamp(restLength - springLength, 0.0f, restLength);
                state.compression01 = restLength > 0.0001f ? Mathf.Clamp01(state.compression / restLength) : 0.0f;
                groundedWheels++;
            }
            else
            {
                AdvanceWheelSpinAirborne(i, deltaTime);
            }

            wheelStates[i] = state;
        }
    }

    private void SimulateWheels(VehicleInputs inputs, float perWheelDriveForce, float brakeForce, float rearBrakeForce, float deltaTime)
    {
        float speedKph = SpeedKph;
        for (int i = 0; i < wheelBindings.Length; i++)
        {
            WheelBinding binding = wheelBindings[i];
            if (binding.hardpoint == null)
                continue;

            float wheelBrakeForce = binding.handbrake ? rearBrakeForce : brakeForce;
            if (!wheelStates[i].grounded)
                continue;

            WheelRuntimeState state = wheelStates[i];
            float springForce = ApplySuspensionForce(binding, state.contactPoint, state.suspensionLength);
            state.springForce = springForce;
            wheelStates[i] = state;

            ApplyTireForces(binding, state, inputs, perWheelDriveForce, wheelBrakeForce, speedKph, deltaTime);

            Quaternion steerRotation = Quaternion.AngleAxis(state.steerAngle, transform.up);
            Vector3 wheelForward = steerRotation * transform.forward;
            Vector3 pointVelocity = GetPointVelocityCustom(state.contactPoint);
            AdvanceWheelSpinFromSurfaceSpeed(i, Vector3.Dot(pointVelocity, wheelForward), deltaTime);
        }

        ApplyAntiRollForces();
    }

    private float ApplySuspensionForce(WheelBinding binding, Vector3 contactPoint, float springLength)
    {
        float restLength = GetSuspensionRestLength();
        float compressionDistance = Mathf.Clamp(restLength - springLength, 0.0f, restLength);
        if (compressionDistance <= 0.0f)
            return 0.0f;

        float sprungMass = GetWheelSprungMass(binding);
        float springRate = CalculateSpringRate(sprungMass, suspensionConfig.suspensionFrequency);
        float damperRate = CalculateDamperRate(springRate, sprungMass, suspensionConfig.suspensionDamping);
        float suspensionVelocity = Vector3.Dot(GetPointVelocityCustom(contactPoint), -transform.up);
        float damperScale = suspensionVelocity >= 0.0f ? compressionDampingScale : reboundDampingScale;
        float damperForce = suspensionVelocity * damperRate * damperScale;
        float maxSpringForce = Mathf.Max(5000.0f, springRate * Mathf.Max(restLength, 0.05f) * 1.5f);
        float maxReboundForce = maxSpringForce * maxReboundForceRatio;
        float forceMagnitude = Mathf.Clamp((compressionDistance * springRate) + damperForce, -maxReboundForce, maxSpringForce);
        if (Mathf.Abs(forceMagnitude) <= 0.01f)
            return 0.0f;

        AddForceAtPosition(transform.up * forceMagnitude, contactPoint);
        return forceMagnitude;
    }

    private void ApplyTireForces(WheelBinding binding, WheelRuntimeState state, VehicleInputs inputs, float perWheelDriveForce, float brakeForce, float speedKph, float deltaTime)
    {
        Quaternion steerRotation = Quaternion.AngleAxis(state.steerAngle, transform.up);
        Vector3 wheelForward = Vector3.ProjectOnPlane(steerRotation * transform.forward, state.contactNormal).normalized;
        Vector3 wheelRight = Vector3.ProjectOnPlane(steerRotation * transform.right, state.contactNormal).normalized;
        if (wheelForward.sqrMagnitude <= 0.0001f || wheelRight.sqrMagnitude <= 0.0001f)
            return;

        Vector3 contactVelocity = GetPointVelocityCustom(state.contactPoint);
        float forwardVelocity = Vector3.Dot(contactVelocity, wheelForward);
        float sideVelocity = Vector3.Dot(contactVelocity, wheelRight);

        float wheelMass = GetWheelSprungMass(binding);
        float normalLoad = Mathf.Max(1.0f, wheelMass * Physics.gravity.magnitude);
        float loadFactor = Mathf.Clamp01(state.springForce / Mathf.Max(normalLoad * 2.5f, 1.0f));
        float gripBlend = groundedWheels > 0 ? landingGripBlend : 0.0f;

        float handbrakeGrip = 1.0f;
        if (inputs.Handbrake && binding.handbrake)
        {
            handbrakeGrip *= Mathf.Clamp(handlingConfig.handbrakeFrictionMultiplier, 0.05f, 1.0f);
            if (handlingConfig.handbrakeSidewaysBySpeed != null && handlingConfig.handbrakeSidewaysBySpeed.length > 0)
                handbrakeGrip *= Mathf.Clamp(handlingConfig.handbrakeSidewaysBySpeed.Evaluate(speedKph), 0.1f, 1.0f);
        }

        float driveForce = binding.drive ? perWheelDriveForce : 0.0f;
        float brakeSignedForce = ComputeBrakeForce(forwardVelocity, brakeForce);
        float rollingForce = brakeForce <= 0.0f ? ComputeRollingResistance(forwardVelocity) : 0.0f;
        float netLongitudinal = driveForce + brakeSignedForce + rollingForce;

        float maxLongitudinal = normalLoad *
                                Mathf.Max(0.5f, handlingConfig.forwardFriction) *
                                longitudinalGripScale *
                                Mathf.Lerp(0.45f, 1.0f, loadFactor) *
                                Mathf.Max(0.15f, gripBlend);
        netLongitudinal = Mathf.Clamp(netLongitudinal, -maxLongitudinal, maxLongitudinal);

        float sideCancelAccel = -sideVelocity / Mathf.Max(deltaTime, 0.0001f);
        float lateralForce = sideCancelAccel * wheelMass;
        float maxLateral = normalLoad *
                           Mathf.Max(0.5f, handlingConfig.sidewaysFriction) *
                           lateralForceScale *
                           handbrakeGrip *
                           Mathf.Lerp(0.35f, 1.0f, loadFactor) *
                           Mathf.Max(0.15f, gripBlend);
        lateralForce = Mathf.Clamp(lateralForce, -maxLateral, maxLateral);

        Vector3 totalForce = (wheelForward * netLongitudinal) + (wheelRight * lateralForce);
        AddForceAtPosition(totalForce, state.contactPoint);
    }

    private void ApplyAntiRollForces()
    {
        if (suspensionConfig == null)
            return;

        ApplyAntiRollAxle(0, 1, suspensionConfig.antiRollFront);
        ApplyAntiRollAxle(2, 3, suspensionConfig.antiRollRear);
    }

    private void ApplyAntiRollAxle(int leftIndex, int rightIndex, float antiRollForce)
    {
        if (antiRollForce <= 0.0f)
            return;

        if (leftIndex < 0 || leftIndex >= wheelStates.Length || rightIndex < 0 || rightIndex >= wheelStates.Length)
            return;

        WheelRuntimeState left = wheelStates[leftIndex];
        WheelRuntimeState right = wheelStates[rightIndex];
        float suspensionDistance = Mathf.Max(0.001f, GetSuspensionDistance());
        float leftTravel = left.grounded ? Mathf.Clamp01(left.suspensionLength / suspensionDistance) : 1.0f;
        float rightTravel = right.grounded ? Mathf.Clamp01(right.suspensionLength / suspensionDistance) : 1.0f;
        float antiRoll = (leftTravel - rightTravel) * antiRollForce;

        if (left.grounded)
            AddForceAtPosition(transform.up * -antiRoll, left.contactPoint);

        if (right.grounded)
            AddForceAtPosition(transform.up * antiRoll, right.contactPoint);
    }

    private void ApplyChassisForces(VehicleInputs inputs, float deltaTime)
    {
        float speed = customLinearVelocity.magnitude;
        if (groundedWheels > 0 && handlingConfig.downforce > 0.0f && speed > 0.01f)
            AddForce(-transform.up * (handlingConfig.downforce * speed));
        else if (groundedWheels == 0 && extraGravityInAir > 0.0f)
        {
            Vector3 gravityDirection = Physics.gravity.sqrMagnitude > 0.0001f ? Physics.gravity.normalized : Vector3.down;
            AddForce(gravityDirection * (extraGravityInAir * body.mass));
        }

        if (speed > 0.01f)
        {
            float dragForce = handlingConfig.rollingResistance + handlingConfig.aerodynamicDrag * speed * speed;
            AddForce(-customLinearVelocity.normalized * dragForce);
        }

        Vector3 localVelocity = transform.InverseTransformDirection(customLinearVelocity);
        Vector3 localAngularVelocity = transform.InverseTransformDirection(customAngularVelocity);
        if (groundedWheels > 0)
        {
            AddForce(-transform.right * localVelocity.x * handlingConfig.lateralStability * body.mass * stabilizerForceScale);
            AddTorque(-transform.up * localAngularVelocity.y * handlingConfig.yawStability * body.mass * stabilizerForceScale);
            AddTorque(-transform.forward * localAngularVelocity.z * handlingConfig.lateralStability * body.mass * stabilizerForceScale);
            AddTorque(-transform.right * localAngularVelocity.x * handlingConfig.yawStability * body.mass * stabilizerForceScale * 0.35f);
        }

        ApplyAssistForces(inputs);

        if (groundedWheels > 0 && simulationState.currentDriftKickForce > 0.0f)
        {
            float steerSign = Mathf.Sign(inputs.Steer);
            if (!Mathf.Approximately(steerSign, 0.0f))
            {
                float directionSign = Mathf.Sign(localVelocity.z);
                if (Mathf.Approximately(directionSign, 0.0f))
                    directionSign = 1.0f;

                float reverseSign = directionSign < 0.0f ? -1.0f : 1.0f;
                Vector3 lateral = -transform.right * steerSign * reverseSign;
                Vector3 force = lateral * simulationState.currentDriftKickForce;
                Vector3 position = transform.position - transform.forward * handlingConfig.driftKickRearOffset * directionSign;
                AddForceAtPosition(force, position);
            }
        }

    }

    private bool TryGetGroundHit(Vector3 hardpointPosition, Vector3 up, float suspensionDistance, float wheelRadius, out RaycastHit hit, out float springLength)
    {
        Vector3 origin = hardpointPosition;
        float castRadius = Mathf.Min(wheelRadius * wheelProbeRadiusScale, wheelRadius * 0.98f);
        float distance = suspensionDistance + wheelRadius + suspensionRayExtraDistance;
        int hitCount = Physics.SphereCastNonAlloc(origin, castRadius, -up, raycastHits, distance, groundMask, QueryTriggerInteraction.Ignore);
        if (hitCount > 0)
        {
            float bestDistance = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit candidate = raycastHits[i];
                if (candidate.collider == null || IsSelfCollider(candidate.collider))
                    continue;
                if (candidate.distance >= bestDistance)
                    continue;

                bestDistance = candidate.distance;
                bestIndex = i;
            }

            if (bestIndex >= 0)
            {
                hit = raycastHits[bestIndex];
                springLength = Mathf.Clamp(hit.distance - wheelRadius, 0.0f, suspensionDistance);
                return true;
            }
        }

        hit = default;
        springLength = suspensionDistance;
        return false;
    }

    private void IntegrateCustomBody(float deltaTime)
    {
        if (body == null)
            return;

        AddForce(Physics.gravity * body.mass);

        float mass = Mathf.Max(50.0f, body.mass);
        customLinearVelocity += (accumulatedForce / mass) * deltaTime;
        customAngularVelocity += ComputeAngularAcceleration(accumulatedTorque) * deltaTime;
        customAngularVelocity = Vector3.ClampMagnitude(customAngularVelocity, maxAngularVelocity);

        Vector3 nextPosition = transform.position;
        Quaternion nextRotation = IntegrateRotation(transform.rotation, customAngularVelocity, deltaTime);

        ResolveBodyTranslation(ref nextPosition, nextRotation, deltaTime);
        ResolveBodyDepenetration(ref nextPosition, nextRotation);
        ApplySimulatedTransform(nextPosition, nextRotation);

        accumulatedForce = Vector3.zero;
        accumulatedTorque = Vector3.zero;
    }

    private void ResolveBodyTranslation(ref Vector3 position, Quaternion rotation, float deltaTime)
    {
        Vector3 remaining = customLinearVelocity * deltaTime;
        for (int iteration = 0; iteration < maxSweepIterations; iteration++)
        {
            float distance = remaining.magnitude;
            if (distance <= 0.0001f)
                break;

            Vector3 direction = remaining / distance;
            int hitCount = Physics.BoxCastNonAlloc(
                GetBodyCenter(position, rotation),
                GetBodyQueryHalfExtents(),
                direction,
                bodySweepHits,
                rotation,
                distance + collisionSkin,
                bodyCollisionMask,
                QueryTriggerInteraction.Ignore);

            if (!TryGetClosestValidHit(bodySweepHits, hitCount, out RaycastHit hit))
            {
                position += remaining;
                remaining = Vector3.zero;
                break;
            }

            float safeDistance = Mathf.Max(0.0f, hit.distance - collisionSkin);
            position += direction * safeDistance;

            ResolveBodyCollision(hit.collider, hit.point, hit.normal);

            remaining = Vector3.ProjectOnPlane(remaining - direction * safeDistance, hit.normal);
            position += hit.normal * Mathf.Min(collisionSkin, 0.01f);
        }

        position += remaining;
    }

    private void ResolveBodyDepenetration(ref Vector3 position, Quaternion rotation)
    {
        for (int iteration = 0; iteration < maxDepenetrationIterations; iteration++)
        {
            int overlapCount = Physics.OverlapBoxNonAlloc(
                GetBodyCenter(position, rotation),
                GetBodyQueryHalfExtents(),
                overlapHits,
                rotation,
                bodyCollisionMask,
                QueryTriggerInteraction.Ignore);

            Vector3 separation = Vector3.zero;
            int penetrationCount = 0;

            for (int i = 0; i < overlapCount; i++)
            {
                Collider other = overlapHits[i];
                if (!IsValidCollisionCandidate(other))
                    continue;

                if (!Physics.ComputePenetration(
                        bodyCollider,
                        position,
                        rotation,
                        other,
                        other.transform.position,
                        other.transform.rotation,
                        out Vector3 direction,
                        out float distance))
                {
                    continue;
                }

                if (distance <= 0.0001f)
                    continue;

                separation += direction * (distance + collisionSkin);
                penetrationCount++;

                Vector3 contactPoint = other.ClosestPoint(GetBodyCenter(position, rotation));
                ResolveBodyCollision(other, contactPoint, direction);
            }

            if (penetrationCount == 0 || separation.sqrMagnitude <= 0.000001f)
                break;

            position += separation / penetrationCount;
        }
    }

    private bool TryGetClosestValidHit(RaycastHit[] hits, int hitCount, out RaycastHit bestHit)
    {
        float bestDistance = float.MaxValue;
        int bestIndex = -1;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = hits[i];
            if (!IsValidCollisionCandidate(candidate.collider))
                continue;

            if (candidate.distance >= bestDistance)
                continue;

            bestDistance = candidate.distance;
            bestIndex = i;
        }

        if (bestIndex >= 0)
        {
            bestHit = hits[bestIndex];
            return true;
        }

        bestHit = default;
        return false;
    }

    private bool IsValidCollisionCandidate(Collider collider)
    {
        if (collider == null || !collider.enabled || collider.isTrigger)
            return false;

        return !IsSelfCollider(collider);
    }

    private bool IsSelfCollider(Collider collider)
    {
        if (collider == null)
            return true;

        return collider == bodyCollider ||
               collider.transform == transform ||
               collider.transform.IsChildOf(transform) ||
               collider.attachedRigidbody == body;
    }

    private void ResolveBodyCollision(Collider otherCollider, Vector3 contactPoint, Vector3 normal)
    {
        if (otherCollider == null || normal.sqrMagnitude <= 0.0001f)
            return;

        Rigidbody otherBody = otherCollider.attachedRigidbody;
        Vector3 collisionNormal = normal.normalized;
        Vector3 carPointVelocity = GetPointVelocityCustom(contactPoint);
        Vector3 otherPointVelocity = otherBody != null && !otherBody.isKinematic
            ? otherBody.GetPointVelocity(contactPoint)
            : Vector3.zero;
        Vector3 relativeVelocity = carPointVelocity - otherPointVelocity;
        float closingSpeed = Vector3.Dot(relativeVelocity, collisionNormal);
        if (closingSpeed >= -0.001f)
            return;

        float carMass = Mathf.Max(50.0f, body != null ? body.mass : 1200.0f);
        float otherMass = otherBody != null && !otherBody.isKinematic
            ? Mathf.Max(0.001f, otherBody.mass)
            : float.PositiveInfinity;
        float inverseMassSum = (1.0f / carMass) + (float.IsPositiveInfinity(otherMass) ? 0.0f : (1.0f / otherMass));
        if (inverseMassSum <= 0.0001f)
            return;

        float impulseMagnitude = -(1.0f + bodyCollisionRestitution) * closingSpeed / inverseMassSum;
        if (impulseMagnitude <= 0.0001f)
            return;

        Vector3 impulse = collisionNormal * impulseMagnitude;
        ApplyImpulseAtPosition(impulse, contactPoint);

        if (otherBody != null && !otherBody.isKinematic)
            otherBody.AddForceAtPosition(-impulse * dynamicBodyPushScale, contactPoint, ForceMode.Impulse);

        BodyCollisionResolved?.Invoke(new BodyCollisionEvent
        {
            point = contactPoint,
            normal = collisionNormal,
            relativeVelocity = relativeVelocity,
            impulse = impulse,
            otherCollider = otherCollider,
            otherBody = otherBody,
            otherBodyDynamic = otherBody != null && !otherBody.isKinematic
        });
    }

    private Vector3 GetPointVelocityCustom(Vector3 worldPoint)
    {
        Vector3 offset = worldPoint - GetCenterOfMassWorld(transform.position, transform.rotation);
        return customLinearVelocity + Vector3.Cross(customAngularVelocity, offset);
    }

    private void AddForce(Vector3 force)
    {
        accumulatedForce += force;
    }

    private void AddForceAtPosition(Vector3 force, Vector3 worldPoint)
    {
        accumulatedForce += force;
        Vector3 leverArm = worldPoint - GetCenterOfMassWorld(transform.position, transform.rotation);
        accumulatedTorque += Vector3.Cross(leverArm, force);
    }

    private void ApplyImpulseAtPosition(Vector3 impulse, Vector3 worldPoint)
    {
        float mass = Mathf.Max(50.0f, body != null ? body.mass : 1200.0f);
        customLinearVelocity += impulse / mass;
        Vector3 leverArm = worldPoint - GetCenterOfMassWorld(transform.position, transform.rotation);
        customAngularVelocity += ComputeAngularVelocityDelta(Vector3.Cross(leverArm, impulse));
        customAngularVelocity = Vector3.ClampMagnitude(customAngularVelocity, maxAngularVelocity);
    }

    private void AddTorque(Vector3 worldTorque)
    {
        accumulatedTorque += worldTorque;
    }

    private void AddRelativeTorque(float x, float y, float z)
    {
        AddTorque(transform.TransformDirection(new Vector3(x, y, z)));
    }

    private Vector3 ComputeAngularAcceleration(Vector3 worldTorque)
    {
        Vector3 size = bodyCollider != null ? bodyCollider.size : new Vector3(1.8f, 0.7f, 4.0f);
        float mass = Mathf.Max(50.0f, body.mass);
        float inertiaX = Mathf.Max(1.0f, mass * (size.y * size.y + size.z * size.z) / 12.0f);
        float inertiaY = Mathf.Max(1.0f, mass * (size.x * size.x + size.z * size.z) / 12.0f);
        float inertiaZ = Mathf.Max(1.0f, mass * (size.x * size.x + size.y * size.y) / 12.0f);

        Vector3 localTorque = Quaternion.Inverse(transform.rotation) * worldTorque;
        Vector3 localAngularAcceleration = new Vector3(
            localTorque.x / inertiaX,
            localTorque.y / inertiaY,
            localTorque.z / inertiaZ);
        return transform.rotation * localAngularAcceleration;
    }

    private Vector3 ComputeAngularVelocityDelta(Vector3 worldAngularImpulse)
    {
        Vector3 size = bodyCollider != null ? bodyCollider.size : new Vector3(1.8f, 0.7f, 4.0f);
        float mass = Mathf.Max(50.0f, body.mass);
        float inertiaX = Mathf.Max(1.0f, mass * (size.y * size.y + size.z * size.z) / 12.0f);
        float inertiaY = Mathf.Max(1.0f, mass * (size.x * size.x + size.z * size.z) / 12.0f);
        float inertiaZ = Mathf.Max(1.0f, mass * (size.x * size.x + size.y * size.y) / 12.0f);

        Vector3 localAngularImpulse = Quaternion.Inverse(transform.rotation) * worldAngularImpulse;
        Vector3 localAngularVelocityDelta = new Vector3(
            localAngularImpulse.x / inertiaX,
            localAngularImpulse.y / inertiaY,
            localAngularImpulse.z / inertiaZ);
        return transform.rotation * localAngularVelocityDelta;
    }

    private Quaternion IntegrateRotation(Quaternion rotation, Vector3 angularVelocity, float deltaTime)
    {
        float angleRadians = angularVelocity.magnitude * deltaTime;
        if (angleRadians <= 0.0001f)
            return rotation;

        Quaternion delta = Quaternion.AngleAxis(angleRadians * Mathf.Rad2Deg, angularVelocity.normalized);
        return (delta * rotation).normalized;
    }

    private Vector3 GetBodyCenter(Vector3 position, Quaternion rotation)
    {
        return position + (rotation * bodyColliderCenterLocal);
    }

    private Vector3 GetCenterOfMassWorld(Vector3 position, Quaternion rotation)
    {
        return position + (rotation * body.centerOfMass);
    }

    private Vector3 GetBodyQueryHalfExtents()
    {
        return new Vector3(
            Mathf.Max(0.05f, bodyColliderHalfExtents.x - collisionSkin),
            Mathf.Max(0.05f, bodyColliderHalfExtents.y - collisionSkin),
            Mathf.Max(0.05f, bodyColliderHalfExtents.z - collisionSkin));
    }

    private void ApplySimulatedTransform(Vector3 position, Quaternion rotation)
    {
        RecordSimulationPose(position, rotation);
        transform.SetPositionAndRotation(position, rotation);
        if (body == null)
            return;

        body.position = position;
        body.rotation = rotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    private void SyncCustomBodyState(bool resetVelocities)
    {
        if (resetVelocities)
        {
            customLinearVelocity = Vector3.zero;
            customAngularVelocity = Vector3.zero;
        }

        accumulatedForce = Vector3.zero;
        accumulatedTorque = Vector3.zero;
        if (body == null)
            return;

        body.position = transform.position;
        body.rotation = transform.rotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = true;
        RecordSimulationPose(transform.position, transform.rotation, true);
    }

    private void RefreshBodyColliderShape()
    {
        if (bodyCollider == null)
        {
            bodyColliderCenterLocal = Vector3.zero;
            bodyColliderHalfExtents = new Vector3(0.9f, 0.35f, 2.0f);
            return;
        }

        bodyColliderCenterLocal = bodyCollider.center;
        Vector3 scaledSize = Vector3.Scale(bodyCollider.size, Abs(transform.lossyScale));
        bodyColliderHalfExtents = scaledSize * 0.5f;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private void RecordSimulationPose(Vector3 position, Quaternion rotation, bool forceReset = false)
    {
        if (!renderPoseInitialized || forceReset)
        {
            previousSimulationPosition = position;
            previousSimulationRotation = rotation;
            currentSimulationPosition = position;
            currentSimulationRotation = rotation;
            renderPoseInitialized = true;
        }
        else
        {
            previousSimulationPosition = currentSimulationPosition;
            previousSimulationRotation = currentSimulationRotation;
            currentSimulationPosition = position;
            currentSimulationRotation = rotation;
        }

        lastSimulationTime = Time.time;
        lastSimulationDeltaTime = Time.inFixedTimeStep ? Time.fixedDeltaTime : Mathf.Max(0.0001f, Time.deltaTime);
    }

    private void UpdateCameraTargetAnchor()
    {
        if (cameraTargetAnchor == null)
            return;

        if (!renderPoseInitialized)
        {
            cameraTargetAnchor.localPosition = Vector3.zero;
            cameraTargetAnchor.localRotation = Quaternion.identity;
            return;
        }

        float delta = Mathf.Max(0.0001f, lastSimulationDeltaTime);
        float alpha = Mathf.Clamp01((Time.time - lastSimulationTime) / delta);
        Vector3 interpolatedPosition = Vector3.Lerp(previousSimulationPosition, currentSimulationPosition, alpha);
        Quaternion interpolatedRotation = Quaternion.Slerp(previousSimulationRotation, currentSimulationRotation, alpha);
        cameraTargetAnchor.localPosition = transform.InverseTransformPoint(interpolatedPosition);
        cameraTargetAnchor.localRotation = Quaternion.Inverse(transform.rotation) * interpolatedRotation;
    }

    private void RestoreVisualHierarchyToSimulationPose()
    {
        if (bodyVisualRoot != null)
        {
            bodyVisualRoot.localPosition = bodyVisualBaseLocalPosition;
            bodyVisualRoot.localRotation = bodyVisualBaseLocalRotation;
        }

        for (int i = 0; i < wheelBindings.Length; i++)
        {
            WheelBinding binding = wheelBindings[i];
            if (binding.hardpoint == null)
                continue;

            binding.hardpoint.localPosition = binding.baseHardpointLocalPosition;
            binding.hardpoint.localRotation = binding.baseHardpointLocalRotation;
        }
    }

    private void ApplyRenderInterpolation()
    {
        if (!renderPoseInitialized)
            return;

        float delta = Mathf.Max(0.0001f, lastSimulationDeltaTime);
        float alpha = Mathf.Clamp01((Time.time - lastSimulationTime) / delta);
        Vector3 interpolatedPosition = Vector3.Lerp(previousSimulationPosition, currentSimulationPosition, alpha);
        Quaternion interpolatedRotation = Quaternion.Slerp(previousSimulationRotation, currentSimulationRotation, alpha);
        Quaternion inverseCurrentRotation = Quaternion.Inverse(transform.rotation);

        if (bodyVisualRoot != null)
        {
            Vector3 bodyWorldPosition = interpolatedPosition + (interpolatedRotation * bodyVisualBaseLocalPosition);
            Quaternion bodyWorldRotation = interpolatedRotation * bodyVisualBaseLocalRotation;
            bodyVisualRoot.localPosition = transform.InverseTransformPoint(bodyWorldPosition);
            bodyVisualRoot.localRotation = inverseCurrentRotation * bodyWorldRotation;
        }

        for (int i = 0; i < wheelBindings.Length; i++)
        {
            WheelBinding binding = wheelBindings[i];
            if (binding.hardpoint == null)
                continue;

            Vector3 hardpointWorldPosition = interpolatedPosition + (interpolatedRotation * binding.baseHardpointLocalPosition);
            Quaternion hardpointWorldRotation = interpolatedRotation * binding.baseHardpointLocalRotation;
            binding.hardpoint.localPosition = transform.InverseTransformPoint(hardpointWorldPosition);
            binding.hardpoint.localRotation = inverseCurrentRotation * hardpointWorldRotation;
        }
    }

    private void UpdateWheelVisuals(float deltaTime)
    {
        for (int i = 0; i < wheelBindings.Length; i++)
        {
            WheelBinding binding = wheelBindings[i];
            if (binding.visualRoot == null)
                continue;

            WheelRuntimeState state = wheelStates[i];
            float targetLength = state.grounded ? state.suspensionLength : GetSuspensionDistance();
            Vector3 targetPosition = Vector3.down * targetLength;
            float t = 1.0f - Mathf.Exp(-12.0f * deltaTime);
            binding.visualRoot.localPosition = Vector3.Lerp(binding.visualRoot.localPosition, targetPosition, t);

            Quaternion steerRotation = binding.steer ? Quaternion.Euler(0.0f, state.steerAngle, 0.0f) : Quaternion.identity;
            Quaternion spinRotation = Quaternion.Euler(state.spinAngle, 0.0f, 0.0f);
            Quaternion targetRotation = steerRotation * spinRotation * binding.baseVisualRotation;
            binding.visualRoot.localRotation = Quaternion.Slerp(binding.visualRoot.localRotation, targetRotation, t);
        }
    }

    private void UpdateSteeringWheelVisual(float deltaTime)
    {
        if (steeringWheel == null || handlingConfig == null)
            return;

        float maxSteerAngle = Mathf.Max(0.01f, handlingConfig.maxSteerAngle);
        float normalized = Mathf.Clamp(simulationState.currentSteerAngle / maxSteerAngle, -1.0f, 1.0f);
        float targetAngle = -normalized * steeringWheelMaxRotation;
        float t = 1.0f - Mathf.Exp(-steeringWheelResponse * deltaTime);
        simulationState.currentSteeringWheelAngle = Mathf.Lerp(simulationState.currentSteeringWheelAngle, targetAngle, t);
        steeringWheel.localRotation = steeringWheelBaseRotation * Quaternion.Euler(0.0f, 0.0f, simulationState.currentSteeringWheelAngle);
    }

    private void AdvanceWheelSpinFromSurfaceSpeed(int wheelIndex, float forwardSpeed, float deltaTime)
    {
        WheelRuntimeState state = wheelStates[wheelIndex];
        if (currentInput.handbrake > 0.01f && wheelBindings[wheelIndex].handbrake)
        {
            state.spinVelocity = 0.0f;
            wheelStates[wheelIndex] = state;
            return;
        }

        float circumference = Mathf.Max(0.01f, 2.0f * Mathf.PI * GetWheelRadius());
        float targetDegreesPerSecond = (forwardSpeed / circumference) * 360.0f;
        float response = 1.0f - Mathf.Exp(-24.0f * deltaTime);
        state.spinVelocity = Mathf.Lerp(state.spinVelocity, targetDegreesPerSecond, response);
        state.spinAngle = Mathf.Repeat(state.spinAngle + state.spinVelocity * deltaTime, 360.0f);
        wheelStates[wheelIndex] = state;
    }

    private void AdvanceWheelSpinAirborne(int wheelIndex, float deltaTime)
    {
        WheelRuntimeState state = wheelStates[wheelIndex];
        if (currentInput.handbrake > 0.01f && wheelBindings[wheelIndex].handbrake)
        {
            state.spinVelocity = 0.0f;
            wheelStates[wheelIndex] = state;
            return;
        }

        state.spinAngle = Mathf.Repeat(state.spinAngle + state.spinVelocity * deltaTime, 360.0f);
        state.spinVelocity *= Mathf.Exp(-1.2f * deltaTime);
        wheelStates[wheelIndex] = state;
    }


    private int CountDriveWheels()
    {
        int count = 0;
        for (int i = 0; i < wheelBindings.Length; i++)
        {
            if (wheelBindings[i].drive)
                count++;
        }

        return Mathf.Max(1, count);
    }

    private float GetWheelSprungMass(WheelBinding binding)
    {
        float totalMass = Mathf.Max(50.0f, body != null ? body.mass : 1200.0f);
        float frontBias = suspensionConfig != null ? Mathf.Clamp01(suspensionConfig.frontWeightBias) : 0.55f;
        float frontMass = totalMass * frontBias;
        float rearMass = totalMass - frontMass;
        bool isFront = binding.steer;
        return Mathf.Max(1.0f, (isFront ? frontMass : rearMass) * 0.5f);
    }

    private bool IsAnyWheelGrounded()
    {
        return groundedWheels > 0;
    }

    private float GetWheelRadius()
    {
        return handlingConfig != null ? Mathf.Clamp(handlingConfig.wheelRadius, 0.05f, 2.0f) : 0.35f;
    }

    private float GetSuspensionDistance()
    {
        return suspensionConfig != null ? Mathf.Clamp(suspensionConfig.suspensionDistance, 0.05f, 0.5f) : 0.2f;
    }

    private float GetSuspensionRestLength()
    {
        float distance = GetSuspensionDistance();
        float targetPosition = suspensionConfig != null ? Mathf.Clamp01(suspensionConfig.suspensionTargetPosition) : 0.5f;
        return Mathf.Clamp(distance * (1.0f - targetPosition), 0.02f, distance);
    }

    private float GetConfiguredSpringStartToWheelCenterDistance()
    {
        if (springStartToWheelCenterDistanceOverride > 0.0f)
            return Mathf.Clamp(springStartToWheelCenterDistanceOverride, 0.02f, 1.0f);

        return GetSuspensionRestLength();
    }

    private static float CalculateSpringRate(float sprungMass, float frequency)
    {
        float omega = 2.0f * Mathf.PI * frequency;
        return omega * omega * sprungMass;
    }

    private static float CalculateDamperRate(float springRate, float sprungMass, float dampingRatio)
    {
        return 2.0f * dampingRatio * Mathf.Sqrt(Mathf.Max(0.0f, springRate * sprungMass));
    }

    private VehicleInput ReadLocalInput(out bool nitro)
    {
        VehicleInput input = default;
        nitro = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            input.throttle = (Keyboard.current.wKey.isPressed ? 1.0f : 0.0f) +
                             (Keyboard.current.sKey.isPressed ? -1.0f : 0.0f);
            input.steer = (Keyboard.current.dKey.isPressed ? 1.0f : 0.0f) +
                          (Keyboard.current.aKey.isPressed ? -1.0f : 0.0f);
            input.brake = 0.0f;
            input.handbrake = Keyboard.current.spaceKey.isPressed ? 1.0f : 0.0f;
            nitro = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
            return SanitizeInput(input);
        }
#else
        input.throttle = Input.GetAxis("Vertical");
        input.steer = Input.GetAxis("Horizontal");
        input.brake = 0.0f;
        input.handbrake = Input.GetKey(KeyCode.Space) ? 1.0f : 0.0f;
        nitro = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        return SanitizeInput(input);
#endif

        return input;
    }

    private void OnDrawGizmos()
    {
        if (!drawDebug)
            return;

        float wheelRadius = GetWheelRadius();
        float suspensionDistance = GetSuspensionDistance();
        float restLength = GetSuspensionRestLength();
        float configuredCenterDistance = GetConfiguredSpringStartToWheelCenterDistance();
        float targetPosition = suspensionConfig != null ? Mathf.Clamp01(suspensionConfig.suspensionTargetPosition) : 0.5f;

        if (bodyCollider != null)
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(GetBodyCenter(transform.position, transform.rotation), transform.rotation, Vector3.one);
            Gizmos.color = new Color(0.3f, 0.7f, 1.0f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, bodyColliderHalfExtents * 2.0f);
            Gizmos.matrix = previousMatrix;

            Vector3 centerOfMassWorld = GetCenterOfMassWorld(transform.position, transform.rotation);
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(centerOfMassWorld, 0.08f);
            Gizmos.DrawRay(centerOfMassWorld, customLinearVelocity * 0.15f);

            if (cameraTargetAnchor != null)
            {
                Gizmos.color = new Color(1.0f, 0.2f, 0.8f, 0.85f);
                Gizmos.DrawWireSphere(cameraTargetAnchor.position, 0.12f);
            }

#if UNITY_EDITOR
            Vector3 configLabelPosition = GetBodyCenter(transform.position, transform.rotation) + transform.up * (bodyColliderHalfExtents.y + 0.7f);
            DrawDebugLabel(
                configLabelPosition,
                $"radius {wheelRadius:0.###}\ntravel {suspensionDistance:0.###}\nphys rest {restLength:0.###}\nlayout {configuredCenterDistance:0.###}\ntarget {targetPosition:0.##}");
#endif
        }

        for (int i = 0; i < wheelBindings.Length; i++)
        {
            WheelBinding binding = wheelBindings[i];
            if (binding.hardpoint == null)
                continue;

            Vector3 origin = binding.hardpoint.position;
            float currentLength = wheelStates[i].grounded ? wheelStates[i].suspensionLength : suspensionDistance;
            Vector3 currentCenter = origin - transform.up * currentLength;
            Vector3 contactPoint = wheelStates[i].grounded
                ? wheelStates[i].contactPoint
                : currentCenter - transform.up * wheelRadius;

            Gizmos.color = new Color(1.0f, 0.85f, 0.15f, 0.95f);
            Gizmos.DrawSphere(origin, 0.04f);

            Gizmos.color = new Color(0.25f, 0.85f, 1.0f, 0.95f);
            Gizmos.DrawSphere(currentCenter, 0.045f);
            Gizmos.DrawWireSphere(currentCenter, wheelRadius);

            Gizmos.color = wheelStates[i].grounded ? Color.green : Color.red;
            Gizmos.DrawSphere(contactPoint, 0.04f);
            if (wheelStates[i].grounded)
                Gizmos.DrawRay(contactPoint, wheelStates[i].contactNormal * 0.25f);

            Gizmos.color = new Color(1.0f, 0.55f, 0.9f, 0.9f);
            Gizmos.DrawLine(origin, currentCenter);

            Gizmos.color = new Color(1.0f, 1.0f, 1.0f, 0.75f);
            Gizmos.DrawLine(currentCenter, contactPoint);

#if UNITY_EDITOR
            Vector3 labelPosition = currentCenter + transform.right * (i % 2 == 0 ? -0.5f : 0.5f) + transform.up * 0.15f;
            DrawDebugLabel(
                labelPosition,
                $"{binding.name}\nlayout start->center {configuredCenterDistance:0.###}\nphys rest {restLength:0.###}\nlive start->center {currentLength:0.###}\ncenterY {currentCenter.y:0.###}\ncontactY {contactPoint.y:0.###}");

            DrawDebugLabel(origin + transform.up * 0.08f, "spring start");
            DrawDebugLabel(currentCenter + transform.right * 0.08f, "wheel center");
            DrawDebugLabel(contactPoint - transform.up * 0.08f, wheelStates[i].grounded ? "ground contact" : "no contact");
#endif
        }
    }

#if UNITY_EDITOR
    private static void DrawDebugLabel(Vector3 worldPosition, string text)
    {
        GUIStyle style = new GUIStyle(EditorStyles.helpBox)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 11,
            richText = false
        };
        style.normal.textColor = Color.white;
        Handles.Label(worldPosition, text, style);
    }
#endif

    private float ComputeBrakeForce(float forwardVelocity, float brakeForce)
    {
        if (Mathf.Abs(forwardVelocity) < 0.01f)
            return 0.0f;

        if (brakeForce > 0.0f)
            return -Mathf.Sign(forwardVelocity) * brakeForce;

        return 0.0f;
    }

    private float ComputeRollingResistance(float forwardVelocity)
    {
        if (Mathf.Abs(forwardVelocity) < 0.01f)
            return 0.0f;

        float resistance = Mathf.Min(Mathf.Abs(forwardVelocity) * handlingConfig.rollingResistance, Mathf.Abs(forwardVelocity) * 1000.0f);
        return -Mathf.Sign(forwardVelocity) * resistance;
    }

    private void ApplyAssistForces(VehicleInputs inputs)
    {
        if (groundedWheels > 0)
        {
            ApplyYawAssist(inputs);
            ApplyUprightAssist(uprightAssist);
        }
        else
        {
            ApplyAirControl(inputs);
            ApplyUprightAssist(uprightAssistInAir);
        }
    }

    private void ApplyYawAssist(VehicleInputs inputs)
    {
        float speed = Mathf.Abs(SpeedForward);
        if (speed < 1.0f || yawAssist <= 0.0f)
            return;

        float desiredYaw = inputs.Steer * yawAssist;
        Vector3 localAngularVelocity = transform.InverseTransformDirection(customAngularVelocity);
        float yawError = desiredYaw - localAngularVelocity.y;
        AddRelativeTorque(0.0f, yawError * 200.0f, 0.0f);
    }

    private void ApplyAirControl(VehicleInputs inputs)
    {
        if (airPitchTorque <= 0.0f && airYawTorque <= 0.0f && airRollTorque <= 0.0f)
            return;

        float pitch = -inputs.Motor * airPitchTorque;
        float yaw = inputs.Steer * airYawTorque;
        float roll = -inputs.Steer * airRollTorque;
        AddRelativeTorque(pitch, yaw, roll);
    }

    private void ApplyUprightAssist(float strength)
    {
        if (strength <= 0.0f)
            return;

        Vector3 currentUp = transform.up;
        Vector3 targetUp = Vector3.up;
        Vector3 torqueAxis = Vector3.Cross(currentUp, targetUp);
        if (torqueAxis.sqrMagnitude <= 0.0001f)
            return;

        float angle = Vector3.Angle(currentUp, targetUp);
        AddTorque(torqueAxis.normalized * angle * strength);
    }

    private void UpdateStateTimers(float deltaTime)
    {
        bool groundedNow = groundedWheels > 0;
        if (groundedNow)
        {
            timeSinceGrounded = 0.0f;
            if (!wasGroundedLastFrame)
                landingGripBlend = landingGripStart;

            airTime = 0.0f;
            landingGripBlend = Mathf.MoveTowards(landingGripBlend, 1.0f, deltaTime / Mathf.Max(0.01f, landingGripBlendTime));
        }
        else
        {
            timeSinceGrounded += deltaTime;
            airTime += deltaTime;
            landingGripBlend = 0.0f;
        }

        wasGroundedLastFrame = groundedNow;
    }

    public VehicleState CaptureState()
    {
        return new VehicleState
        {
            position = transform.position,
            rotation = transform.rotation,
            linearVelocity = customLinearVelocity,
            angularVelocity = customAngularVelocity,
            groundedWheels = groundedWheels,
            airTime = airTime,
            timeSinceGrounded = timeSinceGrounded,
            landingGripBlend = landingGripBlend,
            wasGroundedLastFrame = wasGroundedLastFrame,
            simulation = simulationState
        };
    }

    public void ApplyState(VehicleState state)
    {
        customLinearVelocity = state.linearVelocity;
        customAngularVelocity = state.angularVelocity;
        groundedWheels = state.groundedWheels;
        airTime = state.airTime;
        timeSinceGrounded = state.timeSinceGrounded;
        landingGripBlend = state.landingGripBlend;
        wasGroundedLastFrame = state.wasGroundedLastFrame;
        simulationState = state.simulation;
        ApplySimulatedTransform(state.position, state.rotation);
    }

    private void ApplyAutoBrakeFromOppositeInput(ref VehicleInputs inputs)
    {
        Vector3 localVelocity = transform.InverseTransformDirection(customLinearVelocity);
        float forwardSpeed = localVelocity.z;
        if (engineConfig != null &&
            engineConfig.gearbox != null &&
            engineConfig.gearbox.allowAutoReverse &&
            Mathf.Abs(forwardSpeed) < 0.5f)
        {
            return;
        }

        if (forwardSpeed > 0.5f && inputs.Motor < -0.2f)
        {
            inputs.Brake = true;
            inputs.Motor = 0.0f;
        }
        else if (forwardSpeed < -0.5f && inputs.Motor > 0.2f)
        {
            inputs.Brake = true;
            inputs.Motor = 0.0f;
        }
    }

    private void UpdateNitro(bool nitroInput, float motorInput, float deltaTime)
    {
        if (handlingConfig == null || !handlingConfig.nitroEnabled)
        {
            simulationState.nitroActive = false;
            return;
        }

        bool wantsNitro = nitroInput && motorInput > 0.1f;
        simulationState.nitroActive = wantsNitro && simulationState.nitroAmount > 0.001f;
        float delta = simulationState.nitroActive ? -handlingConfig.nitroDrainPerSecond : handlingConfig.nitroRegenPerSecond;
        simulationState.nitroAmount = Mathf.Clamp01(simulationState.nitroAmount + delta * deltaTime);
    }

    private void UpdatePowertrain(ref SimulationState state, VehicleInputs inputs, float deltaTime)
    {
        EnsureDefaultGears();
        UpdateShiftState(ref state, deltaTime);

        if (engineConfig != null && engineConfig.gearbox != null && engineConfig.gearbox.automatic)
            HandleAutoShift(ref state, inputs);

        UpdateRpm(ref state, inputs, deltaTime);
    }

    private void HandleAutoShift(ref SimulationState state, VehicleInputs inputs)
    {
        Vector3 localVelocity = transform.InverseTransformDirection(customLinearVelocity);
        float forwardSpeed = localVelocity.z;
        float speed = Mathf.Abs(forwardSpeed);
        int maxGear = Mathf.Max(1, engineConfig.gearbox.forwardGears.Count);

        if (engineConfig.gearbox.allowAutoReverse)
        {
            if (forwardSpeed < -0.5f && inputs.Motor < -0.2f && state.currentGear >= 0)
            {
                RequestShift(ref state, -1);
                return;
            }

            if (forwardSpeed > 0.5f && inputs.Motor > 0.2f && state.currentGear <= 0)
            {
                RequestShift(ref state, 1);
                return;
            }

            if (speed < 1.0f)
            {
                if (inputs.Motor < -0.2f && state.currentGear >= 0)
                {
                    RequestShift(ref state, -1);
                    return;
                }

                if (inputs.Motor > 0.2f && state.currentGear <= 0)
                {
                    RequestShift(ref state, 1);
                    return;
                }
            }
        }

        if (state.currentGear <= 0)
            return;

        float minUpshiftRpm = Mathf.Max(engineConfig.engine.maxRpm * 0.9f, engineConfig.gearbox.upshiftRpm);
        if (inputs.Motor > 0.1f && state.currentRpm >= minUpshiftRpm && state.currentGear < maxGear)
        {
            float nextRpm = ComputeCoupledRpm(state.currentGear + 1, state.currentRpm);
            if (nextRpm >= engineConfig.engine.idleRpm)
                RequestShift(ref state, state.currentGear + 1);
        }
        else if (state.currentRpm <= engineConfig.gearbox.downshiftRpm && state.currentGear > 1)
        {
            float nextRpm = ComputeCoupledRpm(state.currentGear - 1, state.currentRpm);
            if (nextRpm <= engineConfig.engine.maxRpm * 0.98f)
                RequestShift(ref state, state.currentGear - 1);
        }
    }

    private void UpdateShiftState(ref SimulationState state, float deltaTime)
    {
        if ((GearShiftState)state.shiftState != GearShiftState.Shifting)
            return;

        if (state.shiftTimer > 0.0f)
            state.shiftTimer = Mathf.Max(0.0f, state.shiftTimer - deltaTime);

        if (state.shiftTimer > 0.0f)
            return;

        state.currentGear = state.requestedGear;
        state.currentRpm = Mathf.Clamp(ComputeCoupledRpm(state.currentGear, state.currentRpm), engineConfig.engine.idleRpm, engineConfig.engine.maxRpm);
        state.shiftState = (int)GearShiftState.Ready;
    }

    private void RequestShift(ref SimulationState state, int targetGear)
    {
        if ((GearShiftState)state.shiftState == GearShiftState.Shifting)
            return;

        int maxGear = Mathf.Max(1, engineConfig.gearbox.forwardGears.Count);
        int clamped = Mathf.Clamp(targetGear, -1, maxGear);
        if (clamped == state.currentGear)
            return;

        state.requestedGear = clamped;
        state.shiftTimer = engineConfig.gearbox.shiftDuration;
        state.shiftState = (int)GearShiftState.Shifting;
        state.shiftTargetRpm = Mathf.Clamp(ComputeCoupledRpm(state.requestedGear, state.currentRpm), engineConfig.engine.idleRpm, engineConfig.engine.maxRpm);
    }

    private void UpdateRpm(ref SimulationState state, VehicleInputs inputs, float deltaTime)
    {
        float targetRpm;

        if ((GearShiftState)state.shiftState == GearShiftState.Shifting)
            targetRpm = state.shiftTargetRpm;
        else if (Mathf.Abs(GetEffectiveGearRatio(state.currentGear, state.shiftState)) > 0.01f)
            targetRpm = ComputeCoupledRpm(state.currentGear, state.currentRpm);
        else
            targetRpm = ComputeFreeRpm(inputs.Motor);

        if (state.currentGear < 0 && inputs.Motor < -0.1f)
            targetRpm = Mathf.Max(targetRpm, ComputeFreeRpm(inputs.Motor));

        targetRpm = Mathf.Clamp(targetRpm, engineConfig.engine.idleRpm, engineConfig.engine.maxRpm);
        state.currentRpm = MoveRpmToward(state.currentRpm, targetRpm, Mathf.Abs(inputs.Motor), deltaTime);
    }

    private float ComputeMotorTorque(SimulationState state, float motorInput)
    {
        if ((GearShiftState)state.shiftState == GearShiftState.Shifting)
            return 0.0f;

        float ratio = GetEffectiveGearRatio(state.currentGear, state.shiftState);
        if (Mathf.Approximately(ratio, 0.0f))
            return 0.0f;

        if (Mathf.Abs(motorInput) > 0.1f && state.currentRpm >= engineConfig.engine.maxRpm * 0.995f)
        {
            float gearSign = Mathf.Sign(state.currentGear);
            if (!Mathf.Approximately(gearSign, 0.0f) && Mathf.Sign(motorInput) == gearSign)
                return 0.0f;
        }

        float signedInput = motorInput;
        if (state.currentGear < 0)
            signedInput = Mathf.Min(0.0f, motorInput);
        else if (state.currentGear > 0)
            signedInput = Mathf.Max(0.0f, motorInput);

        float engineTorque = ComputeEngineTorque(state.currentRpm);
        return signedInput * engineTorque * ratio;
    }

    private void UpdateSteering(ref SimulationState state, VehicleInputs inputs, float deltaTime)
    {
        float steerScale = 1.0f;
        if (handlingConfig.steerBySpeed != null && handlingConfig.steerBySpeed.length > 0)
            steerScale = Mathf.Clamp01(handlingConfig.steerBySpeed.Evaluate(SpeedKph));

        float steerLimit = handlingConfig.maxSteerAngle * steerScale;
        float targetAngle = inputs.Steer * steerLimit;
        float maxStep = handlingConfig.steerResponse * deltaTime * handlingConfig.maxSteerAngle;
        state.currentSteerAngle = Mathf.MoveTowards(state.currentSteerAngle, targetAngle, maxStep);
    }

    private void UpdateDriftKick(ref SimulationState state, VehicleInputs inputs, float deltaTime)
    {
        float targetForce = 0.0f;
        if (inputs.Handbrake && Mathf.Abs(inputs.Steer) >= handlingConfig.driftKickSteerThreshold)
        {
            float curveScale = 1.0f;
            if (handlingConfig.driftKickBySpeed != null && handlingConfig.driftKickBySpeed.length > 0)
                curveScale = Mathf.Clamp01(handlingConfig.driftKickBySpeed.Evaluate(SpeedKph));
            targetForce = curveScale * handlingConfig.driftKickMaxForce;
        }

        float t = 1.0f - Mathf.Exp(-handlingConfig.driftKickResponse * deltaTime);
        state.currentDriftKickForce = Mathf.Lerp(state.currentDriftKickForce, targetForce, t);
    }

    private float ComputeCoupledRpm(int gear, float fallbackRpm)
    {
        float gearRatioAbs = Mathf.Abs(GetGearRatio(gear));
        if (gearRatioAbs <= 0.01f)
            return fallbackRpm;

        float wheelRpm = GetWheelRpmFromSpeed();
        return wheelRpm * gearRatioAbs;
    }

    private float ComputeFreeRpm(float motorInput)
    {
        float throttle = Mathf.Abs(motorInput);
        return Mathf.Lerp(engineConfig.engine.idleRpm, engineConfig.engine.maxRpm, throttle);
    }

    private float MoveRpmToward(float currentRpm, float targetRpm, float throttle, float deltaTime)
    {
        float rpmDelta = targetRpm - currentRpm;
        if (Mathf.Approximately(rpmDelta, 0.0f))
            return targetRpm;

        float powerT = Mathf.InverseLerp(60.0f, 3000.0f, engineConfig.horsepower);
        float responseScale = Mathf.Clamp(engineConfig.engine.rpmResponse / 8.0f, 0.6f, 3.0f);
        if (simulationState.nitroActive)
            responseScale *= Mathf.Max(1.0f, handlingConfig.nitroRpmResponseMultiplier);
        float accelRate = Mathf.Lerp(2000.0f, 12000.0f, powerT) * responseScale;
        float decelRate = Mathf.Lerp(3000.0f, 14000.0f, powerT) * responseScale;
        accelRate *= Mathf.Lerp(0.5f, 1.0f, throttle);
        decelRate *= Mathf.Lerp(2.0f, 1.0f, throttle);
        float maxStep = (rpmDelta > 0.0f ? accelRate : decelRate) * deltaTime;
        float step = Mathf.Clamp(rpmDelta, -maxStep, maxStep);
        return currentRpm + step;
    }

    private float GetWheelRpmFromSpeed()
    {
        float speed = customLinearVelocity.magnitude;
        float circumference = Mathf.Max(0.01f, 2.0f * Mathf.PI * GetWheelRadius());
        return speed / circumference * 60.0f;
    }

    private float ComputeEngineTorque(float rpm)
    {
        float hp = engineConfig.horsepower;
        if (simulationState.nitroActive)
            hp *= Mathf.Max(1.0f, handlingConfig.nitroPowerMultiplier);

        float baseTorque = HorsepowerToTorque(hp);
        float normalized = Mathf.InverseLerp(engineConfig.engine.idleRpm, engineConfig.engine.maxRpm, rpm);
        float peak = 0.55f;
        float width = 0.55f;
        float torqueShape = Mathf.Clamp01(1.0f - Mathf.Abs(normalized - peak) / width);
        float torqueFactor = Mathf.Lerp(0.7f, 1.1f, torqueShape);
        if (engineConfig.powerCurve != null && engineConfig.powerCurve.length > 0)
            torqueFactor *= Mathf.Clamp(engineConfig.powerCurve.Evaluate(normalized), 0.1f, 2.0f);
        float curveTorque = baseTorque * torqueFactor;
        float omega = Mathf.Max(1.0f, rpm) * Mathf.Deg2Rad * 6.0f;
        float powerWatts = hp * 745.7f;
        float powerLimitedTorque = powerWatts / omega;
        return Mathf.Min(curveTorque, powerLimitedTorque);
    }

    private float GetEffectiveGearRatio(int currentGear, int shiftState)
    {
        if ((GearShiftState)shiftState == GearShiftState.Shifting)
            return 0.0f;

        return GetGearRatio(currentGear);
    }

    private float GetGearRatio(int gear)
    {
        if (engineConfig == null || engineConfig.gearbox == null || engineConfig.gearbox.forwardGears == null || engineConfig.gearbox.forwardGears.Count == 0)
            return 0.0f;

        if (gear > 0)
        {
            int index = Mathf.Clamp(gear - 1, 0, engineConfig.gearbox.forwardGears.Count - 1);
            return engineConfig.gearbox.forwardGears[index] * engineConfig.gearbox.finalDrive;
        }

        if (gear < 0)
            return engineConfig.gearbox.reverseRatio * engineConfig.gearbox.finalDrive;

        return 0.0f;
    }

    private static float HorsepowerToTorque(float hp)
    {
        const float wattsPerHp = 745.7f;
        const float rpmAtPeak = 5000.0f;
        float watts = hp * wattsPerHp;
        float omega = rpmAtPeak * Mathf.Deg2Rad * 6.0f;
        return watts / Mathf.Max(1.0f, omega);
    }

    private void EnsureDefaultGears()
    {
        if (engineConfig == null || engineConfig.gearbox == null)
            return;

        if (engineConfig.gearbox.forwardGears == null)
            engineConfig.gearbox.forwardGears = new List<float>();

        if (engineConfig.gearbox.forwardGears.Count == 0)
        {
            engineConfig.gearbox.forwardGears.Add(3.1f);
            engineConfig.gearbox.forwardGears.Add(2.2f);
            engineConfig.gearbox.forwardGears.Add(1.6f);
            engineConfig.gearbox.forwardGears.Add(1.2f);
            engineConfig.gearbox.forwardGears.Add(1.0f);
        }
    }
}
