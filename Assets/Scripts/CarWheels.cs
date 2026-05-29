using System;
using UnityEngine;

namespace DrivingSim
{
    /// <summary>
    /// Data container for a single wheel, pairing a collider with its visual mesh.
    /// </summary>
    [Serializable]
    public struct Wheel
    {
        [Tooltip("WheelCollider used for physics.")]
        public WheelCollider collider;

        [Tooltip("Transform of the visible wheel mesh (child object).")]
        public Transform mesh;
    }

    /// <summary>
    /// High-level wheel layout for a typical 4-wheel car:
    /// Front Left, Front Right, Rear Left, Rear Right.
    ///
    /// This component is responsible for organizing references.
    /// It does NOT apply any forces or control the car behaviour.
    /// </summary>
    public class CarWheels : MonoBehaviour
    {
        [Header("Car body")]
        [Tooltip("Root transform of the car body (usually the object with the Rigidbody).")]
        [SerializeField] private Transform body;

        [Header("Wheel setup")]
        public Wheel frontLeft;
        public Wheel frontRight;
        public Wheel rearLeft;
        public Wheel rearRight;

        /// <summary>
        /// Exposes the car body transform.
        /// </summary>
        public Transform Body => body;

        /// <summary>
        /// Enumerates all wheels for convenience.
        /// </summary>
        public Wheel[] AllWheels => new[] { frontLeft, frontRight, rearLeft, rearRight };
    }
}
