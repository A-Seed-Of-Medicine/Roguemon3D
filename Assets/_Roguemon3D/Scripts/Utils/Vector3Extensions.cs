using UnityEngine;

namespace UnityUtils {
    public static class Vector3Extensions {
        /// <summary>
        /// Sets any x y z values of a Vector3
        /// </summary>
        public static Vector3 With(this Vector3 vector, float? x = null, float? y = null, float? z = null) {
            return new Vector3(x ?? vector.x, y ?? vector.y, z ?? vector.z);
        }

        /// <summary>
        /// Adds to any x y z values of a Vector3
        /// </summary>
        public static Vector3 Add(this Vector3 vector, float x = 0, float y = 0, float z = 0) {
            return new Vector3(vector.x + x, vector.y + y, vector.z + z);
        }

        /// <summary>
        /// Returns a Boolean indicating whether the current Vector3 is in a given range from another Vector3
        /// </summary>
        /// <param name="current">The current Vector3 position</param>
        /// <param name="target">The Vector3 position to compare against</param>
        /// <param name="range">The range value to compare against</param>
        /// <returns>True if the current Vector3 is in the given range from the target Vector3, false otherwise</returns>
        public static bool InRangeOf(this Vector3 current, Vector3 target, float range) {
            return (current - target).sqrMagnitude <= range * range;
        }
        
        /// <summary>
        /// Divides two Vector3 objects component-wise.
        /// </summary>
        /// <remarks>
        /// For each component in v0 (x, y, z), it is divided by the corresponding component in v1 if the component in v1 is not zero. 
        /// Otherwise, the component in v0 remains unchanged.
        /// </remarks>
        /// <example>
        /// Use 'ComponentDivide' to scale a game object proportionally:
        /// <code>
        /// myObject.transform.localScale = originalScale.ComponentDivide(targetDimensions);
        /// </code>
        /// This scales the object size to fit within the target dimensions while maintaining its original proportions.
        ///</example>
        /// <param name="v0">The Vector3 object that this method extends.</param>
        /// <param name="v1">The Vector3 object by which v0 is divided.</param>
        /// <returns>A new Vector3 object resulting from the component-wise division.</returns>
        public static Vector3 ComponentDivide(this Vector3 v0, Vector3 v1){
            return new Vector3( 
                v1.x != 0 ? v0.x / v1.x : v0.x, 
                v1.y != 0 ? v0.y / v1.y : v0.y, 
                v1.z != 0 ? v0.z / v1.z : v0.z);  
        }
        
        /// <summary>
        /// Converts a Vector2 to a Vector3 with a y value of 0.
        /// </summary>
        /// <param name="v2">The Vector2 to convert.</param>
        /// <returns>A Vector3 with the x and z values of the Vector2 and a y value of 0.</returns>
        public static Vector3 ToVector3(this Vector2 v2) {
            return new Vector3(v2.x, 0, v2.y);
        }
        
        /// <summary>
        /// Adds a random offset to the components of a <see cref="Vector3"/> within the specified range.
        /// </summary>
        /// <param name="vector">The original vector to which the random offset will be applied.</param>
        /// <param name="range">The maximum absolute value of random offsets that can be added 
        /// or subtracted to/from each component of the vector.</param>
        /// <returns>A new <see cref="Vector3"/> with random offsets applied to its X, Y, and Z components.
        /// Each offset is in the range [-<paramref name="range"/>, <paramref name="range"/>].</returns>
        public static Vector3 RandomOffset(this Vector3 vector, float range) {
            return vector + new Vector3(
                Random.Range(-range, range),
                Random.Range(-range, range),
                Random.Range(-range, range)
            );
        }        

        /// <summary>
        /// Computes a random point in an annulus (a ring-shaped area) based on minimum and 
        /// maximum radius values around a central Vector3 point (origin).
        /// </summary>
        /// <param name="origin">The center Vector3 point of the annulus.</param>
        /// <param name="minRadius">Minimum radius of the annulus.</param>
        /// <param name="maxRadius">Maximum radius of the annulus.</param>
        /// <returns>A random Vector3 point within the specified annulus.</returns>
        public static Vector3 RandomPointInAnnulus(this Vector3 origin, float minRadius, float maxRadius) {
            float angle = Random.value * Mathf.PI * 2f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    
            // Squaring and then square-rooting radii to ensure uniform distribution within the annulus
            float minRadiusSquared = minRadius * minRadius;
            float maxRadiusSquared = maxRadius * maxRadius;
            float distance = Mathf.Sqrt(Random.value * (maxRadiusSquared - minRadiusSquared) + minRadiusSquared);
    
            // Converting the 2D direction vector to a 3D position vector
            Vector3 position = new Vector3(direction.x, 0, direction.y) * distance;
            return origin + position;
        }
        
        /// <summary>
        /// Calculates the signed angle between two vectors on a plane defined by a normal vector.
        /// </summary>
        /// <param name="vector1">The first vector.</param>
        /// <param name="vector2">The second vector.</param>
        /// <param name="planeNormal">The normal vector of the plane on which to calculate the angle.</param>
        /// <returns>The signed angle between the vectors in degrees.</returns>
        public static float GetAngle(Vector3 vector1, Vector3 vector2, Vector3 planeNormal) {
            var angle = Vector3.Angle(vector1, vector2);
            var sign = Mathf.Sign(Vector3.Dot(planeNormal, Vector3.Cross(vector1, vector2)));
            return angle * sign;
        }

        /// <summary>
        /// Calculates the dot product of a vector and a normalized direction.
        /// </summary>
        /// <param name="vector">The vector to project.</param>
        /// <param name="direction">The direction vector to project onto.</param>
        /// <returns>The dot product of the vector and the direction.</returns>
        public static float GetNormalDotProduct(this Vector3 vector, Vector3 direction) => 
            Vector3.Dot(vector.normalized, direction.normalized);

        /// <summary>
        /// Removes the component of a vector that is in the direction of a given vector.
        /// </summary>
        /// <param name="vector">The vector from which to remove the component.</param>
        /// <param name="direction">The direction vector whose component should be removed.</param>
        /// <returns>The vector with the specified direction removed.</returns>
        public static Vector3 RemoveDotVector(this Vector3 vector, Vector3 direction) {
            return vector - Vector3.Dot(vector, direction.normalized) * direction.normalized;
        }

        /// <summary>
        /// Extracts and returns the component of a vector that is in the direction of a given vector.
        /// </summary>
        /// <param name="vector">The vector from which to extract the component.</param>
        /// <param name="direction">The direction vector to extract along.</param>
        /// <returns>The component of the vector in the direction of the given vector.</returns>
        public static Vector3 ExtractDotVector(this Vector3 vector, Vector3 direction) {
            return Vector3.Dot(vector, direction.normalized) * direction.normalized;
        }

        /// <summary>
        /// Rotates a vector onto a plane defined by a normal vector using a specified up direction.
        /// </summary>
        /// <param name="vector">The vector to be rotated onto the plane.</param>
        /// <param name="planeNormal">The normal vector of the target plane.</param>
        /// <param name="upDirection">The current 'up' direction used to determine the rotation.</param>
        /// <returns>The vector after being rotated onto the specified plane.</returns>
        public static Vector3 RotateVectorOntoPlane(Vector3 vector, Vector3 planeNormal, Vector3 upDirection) {
            // Calculate rotation;
            var rotation = Quaternion.FromToRotation(upDirection, planeNormal);

            // Apply rotation to vector;
            vector = rotation * vector;

            return vector;
        }

        /// <summary>
        /// Projects a given point onto a line defined by a starting position and direction vector.
        /// </summary>
        /// <param name="lineStartPosition">The starting position of the line.</param>
        /// <param name="lineDirection">The direction vector of the line, which should be normalized.</param>
        /// <param name="point">The point to project onto the line.</param>
        /// <returns>The projected point on the line closest to the original point.</returns>
        public static Vector3 ProjectPointOntoLine(Vector3 lineStartPosition, Vector3 lineDirection, Vector3 point) {
            var projectLine = point - lineStartPosition;
            var dotProduct = Vector3.Dot(projectLine, lineDirection);

            return lineStartPosition + lineDirection * dotProduct;
        }

        /// <summary>
        /// Increments a vector toward a target vector at a specified speed over a given time interval.
        /// </summary>
        /// <param name="currentVector">The current vector to be incremented.</param>
        /// <param name="speed">The speed at which to move towards the target vector.</param>
        /// <param name="deltaTime">The time interval over which to move.</param>
        /// <param name="targetVector">The target vector to approach.</param>
        /// <returns>The new vector incremented toward the target vector by the specified speed and time interval.</returns>
        public static Vector3 IncrementVectorTowardTargetVector(Vector3 currentVector, float speed, float deltaTime,
            Vector3 targetVector) {
            return Vector3.MoveTowards(currentVector, targetVector, speed * deltaTime);
        }
    }
}
