using System.Collections.Generic;
using UnityEngine;

public static class VehicleDynamics
{
    public struct Inputs
    {
        public float Motor;
        public float Steer;
        public bool Brake;
        public bool Handbrake;
    }

    public struct Params
    {
        public float brakePower;
        public float handbrakePower;
        public float forwardFriction;
        public float sidewaysFriction;
        public float handbrakeFrictionMultiplier;
        public float downforce;
        public float rollingResistance;
        public float aerodynamicDrag;
        public float forwardExtremumSlip;
        public float forwardExtremumValue;
        public float forwardAsymptoteSlip;
        public float forwardAsymptoteValue;
        public float sidewaysExtremumSlip;
        public float sidewaysExtremumValue;
        public float sidewaysAsymptoteSlip;
        public float sidewaysAsymptoteValue;
        public float antiRollFront;
        public float antiRollRear;
        public float lateralStability;
        public float yawStability;
        public AnimationCurve handbrakeSidewaysBySpeed;
        public float speedKph;
        public float driftKickForce;
        public float driftKickRearOffset;
        public float driftKickSteerInput;
    }

    public struct DebugData
    {
        public Vector3 downforce;
        public Vector3 dragForce;
        public Vector3 stabilityForce;
        public Vector3 stabilityTorque;
        public Vector3 driftForce;
        public Vector3 driftForcePosition;
        public bool driftApplied;
        public float motorTorque;
        public float brakeTorque;
        public float rearBrakeTorque;
        public float steerAngle;
        public float speedKph;
    }

    public static void Apply(
        Rigidbody rb,
        Transform root,
        List<CarControllerBase.Wheel> wheels,
        Inputs inputs,
        float motorTorque,
        float steerAngle,
        Params p,
        ref DebugData debugData)
    {
        float brakeTorque = inputs.Brake ? p.brakePower : 0.0f;
        float rearBrakeTorque = brakeTorque + (inputs.Handbrake ? p.handbrakePower : 0.0f);
        float speedKph = p.speedKph;
        debugData = new DebugData
        {
            motorTorque = motorTorque,
            brakeTorque = brakeTorque,
            rearBrakeTorque = rearBrakeTorque,
            steerAngle = steerAngle,
            speedKph = speedKph
        };

        int driveCount = 0;
        for (int i = 0; i < wheels.Count; i++)
        {
            if (wheels[i].Drive)
                driveCount++;
        }

        float perWheelTorque = driveCount > 0 ? motorTorque / driveCount : 0.0f;
        for (int i = 0; i < wheels.Count; i++)
        {
            CarControllerBase.Wheel wheel = wheels[i];
            ApplyWheelFriction(wheel, inputs.Handbrake, speedKph, p);

            if (wheel.Steer)
                wheel.Collider.steerAngle = steerAngle;

            wheel.Collider.motorTorque = wheel.Drive ? perWheelTorque : 0.0f;
            wheel.Collider.brakeTorque = wheel.Handbrake ? rearBrakeTorque : brakeTorque;
        }

        ApplyAntiRoll(rb, wheels, p.antiRollFront, p.antiRollRear);
        debugData.downforce = ApplyDownforce(rb, root, wheels, p.downforce);
        debugData.dragForce = ApplyDrag(rb, p.rollingResistance, p.aerodynamicDrag);
        ApplyStabilityForces(rb, root, wheels, p.lateralStability, p.yawStability, out debugData.stabilityForce, out debugData.stabilityTorque);
        ApplyDriftKick(rb, root, wheels, p.driftKickForce, p.driftKickRearOffset, p.driftKickSteerInput, out debugData.driftForce, out debugData.driftForcePosition, out debugData.driftApplied);
    }

    private static void ApplyWheelFriction(
        CarControllerBase.Wheel wheel,
        bool handbrake,
        float speedKph,
        Params p)
    {
        float sideways = p.sidewaysFriction;
        float forward = p.forwardFriction;

        if (handbrake && wheel.Handbrake)
        {
            sideways *= p.handbrakeFrictionMultiplier;
            forward *= p.handbrakeFrictionMultiplier;
            if (p.handbrakeSidewaysBySpeed != null && p.handbrakeSidewaysBySpeed.length > 0)
                sideways *= Mathf.Clamp(p.handbrakeSidewaysBySpeed.Evaluate(speedKph), 0.1f, 1.0f);
        }

        WheelFrictionCurve forwardCurve = wheel.Collider.forwardFriction;
        forwardCurve.extremumSlip = p.forwardExtremumSlip;
        forwardCurve.extremumValue = p.forwardExtremumValue;
        forwardCurve.asymptoteSlip = p.forwardAsymptoteSlip;
        forwardCurve.asymptoteValue = p.forwardAsymptoteValue;
        forwardCurve.stiffness = forward;
        wheel.Collider.forwardFriction = forwardCurve;

        WheelFrictionCurve sidewaysCurve = wheel.Collider.sidewaysFriction;
        sidewaysCurve.extremumSlip = p.sidewaysExtremumSlip;
        sidewaysCurve.extremumValue = p.sidewaysExtremumValue;
        sidewaysCurve.asymptoteSlip = p.sidewaysAsymptoteSlip;
        sidewaysCurve.asymptoteValue = p.sidewaysAsymptoteValue;
        sidewaysCurve.stiffness = sideways;
        wheel.Collider.sidewaysFriction = sidewaysCurve;
    }

    private static Vector3 ApplyDownforce(
        Rigidbody rb,
        Transform root,
        List<CarControllerBase.Wheel> wheels,
        float downforce)
    {
        if (downforce <= 0.0f)
            return Vector3.zero;

        float speed = rb.linearVelocity.magnitude;
        if (speed <= 0.01f)
            return Vector3.zero;

        if (!IsAnyWheelGrounded(wheels))
            return Vector3.zero;

        Vector3 force = -root.up * (downforce * speed);
        rb.AddForce(force);
        return force;
    }

    private static Vector3 ApplyDrag(Rigidbody rb, float rollingResistance, float aerodynamicDrag)
    {
        float speed = rb.linearVelocity.magnitude;
        if (speed <= 0.01f)
            return Vector3.zero;

        float dragForce = rollingResistance + aerodynamicDrag * speed * speed;
        Vector3 force = -rb.linearVelocity.normalized * dragForce;
        rb.AddForce(force);
        return force;
    }

    private static void ApplyAntiRoll(
        Rigidbody rb,
        List<CarControllerBase.Wheel> wheels,
        float frontForce,
        float rearForce)
    {
        WheelCollider frontLeft = null;
        WheelCollider frontRight = null;
        WheelCollider rearLeft = null;
        WheelCollider rearRight = null;

        for (int i = 0; i < wheels.Count; i++)
        {
            WheelCollider wc = wheels[i].Collider;
            bool isFront = wc.transform.localPosition.z > 0.0f;
            bool isLeft = wc.transform.localPosition.x < 0.0f;

            if (isFront && isLeft) frontLeft = wc;
            if (isFront && !isLeft) frontRight = wc;
            if (!isFront && isLeft) rearLeft = wc;
            if (!isFront && !isLeft) rearRight = wc;
        }

        ApplyAntiRollAxle(rb, frontLeft, frontRight, frontForce);
        ApplyAntiRollAxle(rb, rearLeft, rearRight, rearForce);
    }

    private static void ApplyAntiRollAxle(
        Rigidbody rb,
        WheelCollider left,
        WheelCollider right,
        float antiRollForce)
    {
        if (antiRollForce <= 0.0f)
            return;

        if (left == null || right == null)
            return;

        float leftTravel = 1.0f;
        float rightTravel = 1.0f;

        if (left.GetGroundHit(out WheelHit leftHit))
            leftTravel = (-left.transform.InverseTransformPoint(leftHit.point).y - left.radius) / left.suspensionDistance;

        if (right.GetGroundHit(out WheelHit rightHit))
            rightTravel = (-right.transform.InverseTransformPoint(rightHit.point).y - right.radius) / right.suspensionDistance;

        float antiRoll = (leftTravel - rightTravel) * antiRollForce;

        if (left.GetGroundHit(out _))
            rb.AddForceAtPosition(left.transform.up * -antiRoll, left.transform.position);

        if (right.GetGroundHit(out _))
            rb.AddForceAtPosition(right.transform.up * antiRoll, right.transform.position);
    }

    private static void ApplyStabilityForces(
        Rigidbody rb,
        Transform root,
        List<CarControllerBase.Wheel> wheels,
        float lateralStability,
        float yawStability,
        out Vector3 force,
        out Vector3 torque)
    {
        force = Vector3.zero;
        torque = Vector3.zero;
        if (!IsAnyWheelGrounded(wheels))
            return;

        if (lateralStability > 0.0f)
        {
            Vector3 localVel = root.InverseTransformDirection(rb.linearVelocity);
            Vector3 lateralVel = root.right * localVel.x;
            force = -lateralVel * lateralStability * rb.mass;
            rb.AddForce(force, ForceMode.Force);
        }

        if (yawStability > 0.0f)
        {
            Vector3 localAngular = root.InverseTransformDirection(rb.angularVelocity);
            torque = -root.up * localAngular.y * yawStability * rb.mass;
            rb.AddTorque(torque, ForceMode.Force);
        }
    }

    private static bool IsAnyWheelGrounded(List<CarControllerBase.Wheel> wheels)
    {
        for (int i = 0; i < wheels.Count; i++)
        {
            if (wheels[i].Collider.GetGroundHit(out _))
                return true;
        }

        return false;
    }

    private static void ApplyDriftKick(
        Rigidbody rb,
        Transform root,
        List<CarControllerBase.Wheel> wheels,
        float kickForce,
        float rearOffset,
        float steerInput,
        out Vector3 force,
        out Vector3 position,
        out bool applied)
    {
        force = Vector3.zero;
        position = root.position;
        applied = false;
        if (kickForce <= 0.0f)
            return;

        if (!IsAnyWheelGrounded(wheels))
            return;

        float steerSign = Mathf.Sign(steerInput);
        if (Mathf.Approximately(steerSign, 0.0f))
            return;

        Vector3 localVel = root.InverseTransformDirection(rb.linearVelocity);
        float directionSign = Mathf.Sign(localVel.z);
        if (Mathf.Approximately(directionSign, 0.0f))
            directionSign = 1.0f;

        float reverseSign = directionSign < 0.0f ? -1.0f : 1.0f;
        Vector3 lateral = -root.right * steerSign * reverseSign;
        force = lateral * kickForce;
        float offset = Mathf.Max(0.0f, rearOffset);
        position = root.position - root.forward * offset * directionSign;
        rb.AddForceAtPosition(force, position, ForceMode.Force);
        applied = true;
    }
}
