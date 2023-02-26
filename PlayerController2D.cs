using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace CandyCoded.PlayerController2D
{

    [RequireComponent(typeof(BoxCollider2D))]
    [RequireComponent(typeof(InputManager))]
    public class PlayerController2D : MonoBehaviour
    {

        [Serializable]
        public class EventWithState : UnityEvent<STATE>
        {
        }

        [Serializable]
        public class EventWithStateComparison : UnityEvent<STATE, STATE>
        {
        }

        [Serializable]
        public struct ToggleableStates
        {
            public bool wallJumping;
            public bool wallSticking;
        }

        public struct MovementBounds
        {
            public float left;
            public float right;
            public float top;
            public float bottom;
        }

        [Serializable]
        public struct LayerMaskGroup
        {
            public LayerMask left;
            public LayerMask right;
            public LayerMask top;
            public LayerMask bottom;
        }

        public const float DEFAULT_HORIZONTAL_SPEED = 7.0f;
        public const float DEFAULT_AIR_RESISTANCE = 0.02f;
        //public const float DEFAULT_LOW_JUMP_SPEED = 10.0f;
        public const float DEFAULT_HIGH_JUMP_SPEED = 15.0f;
        public static readonly Vector2 DEFAULT_GRAVITY = new Vector2(0, -30f);
        //public const float DEFAULT_WALL_SLIDE_SPEED = -2.0f;
        //public const float DEFAULT_WALL_STICK_TRANSITION_DELAY = 0.2f;
        public const int DEFAULT_MAX_AVAILABLE_JUMPS = 2;
        public const float EDGE_COLLIDE_PREVENTION_RATIO = 0.1f;

        public float horizontalSpeed = DEFAULT_HORIZONTAL_SPEED;
        public float airResistance = DEFAULT_AIR_RESISTANCE;
        //public float lowJumpSpeed = DEFAULT_LOW_JUMP_SPEED;
        public float highJumpSpeed = DEFAULT_HIGH_JUMP_SPEED;
        public Vector2 gravity = DEFAULT_GRAVITY;
        //public float wallSlideSpeed = DEFAULT_WALL_SLIDE_SPEED;
        //public float wallStickTransitionDelay = DEFAULT_WALL_STICK_TRANSITION_DELAY;
        public int maxAvailableJumps = DEFAULT_MAX_AVAILABLE_JUMPS;
        public float edgeCollidePreventionRatio = EDGE_COLLIDE_PREVENTION_RATIO;

        public LayerMaskGroup layerMask = new LayerMaskGroup();

        public ToggleableStates toggleableStates = new ToggleableStates { wallJumping = true, wallSticking = true };

#pragma warning disable CS0649
        [SerializeField]
        private bool displayDebugColliders;
#pragma warning restore CS0649

        public EventWithStateComparison StateSwitch;
        public EventWithState StateLoop;

        private Vector2 _velocity = Vector2.zero;

        public Vector2 position { get; private set; } = Vector2.zero;
        public Vector2 velocity => _velocity;

        private const float frictionRaycastRadius = 0.2f;

        private InputManager inputManager;
        private BoxCollider2D boxCollider;
        private Vector3 extents;

        private float verticalFriction;
        private float horizontalFriction;

        private Vector2 verticalExtents;
        private Vector2 horizontalExtents;

        private int currentAvailableJumps;

        public enum STATE
        {
            Idle,
            Walking,
            Running,
            Fall,
            Jump,
            VerticalMovement,
            WallSliding,
            WallSticking,
            WallJump,
            WallDismount
        }

        private STATE _state = STATE.Idle;

        public STATE state
        {

            get { return _state; }

            set
            {

                if (!_state.Equals(value))
                {

                    var previousState = _state;


                    Debug.Log(string.Format("Switched from state {0} to {1}.", _state, value));

                    _state = value;

                    RunStateSwitch();

                    StateSwitch?.Invoke(previousState, value);

                }

            }

        }

        private void Awake()
        {

            inputManager = gameObject.GetComponent<InputManager>();
            boxCollider = gameObject.GetComponent<BoxCollider2D>();

            extents = boxCollider.bounds.extents;

            verticalExtents = new Vector2(0, extents.y);
            horizontalExtents = new Vector2(extents.x, 0);

        }

        private void Update()
        {

            gameObject.transform.position = position;

        }

        private void FixedUpdate()
        {

            position = gameObject.transform.position;

            RunStateLoop();

            inputManager.Reset();

        }

        private void RunStateSwitch()
        {

            if (state.Equals(STATE.Idle)) StateIdleSwitch();
            else if (state.Equals(STATE.Walking)) StateWalkingSwitch();
            else if (state.Equals(STATE.Running)) StateRunningSwitch();
            else if (state.Equals(STATE.Fall)) StateFallSwitch();
            else if (state.Equals(STATE.Jump)) StateJumpSwitch();
            else if (state.Equals(STATE.WallJump)) StateWallJumpingSwitch();
            else if (state.Equals(STATE.WallSliding)) StateWallSlidingSwitch();
            else if (state.Equals(STATE.WallDismount)) StateWallDismountSwitch();

        }

        private void RunStateLoop()
        {

            if (state.Equals(STATE.Idle)) StateIdleLoop();
            else if (state.Equals(STATE.Walking)) StateWalkingLoop();
            else if (state.Equals(STATE.Running)) StateRunningLoop();
            else if (state.Equals(STATE.VerticalMovement)) StateVerticalMovementLoop();
            else if (state.Equals(STATE.WallSliding)) StateWallSlidingLoop();
            else if (state.Equals(STATE.WallSticking)) StateWallStickingLoop();

            StateLoop?.Invoke(state);

        }

        private void Loop()
        {

            var bounds = CalculateMovementBounds();

            position = MoveStep(bounds);

            LoopStateSwitch(bounds);

        }

        private void LoopStateSwitch(MovementBounds bounds)
        {

            if (IsIdle(bounds)) state = STATE.Idle;
            else if (IsRunning(bounds)) state = STATE.Running;
            else if (IsWallDismounting(bounds)) state = STATE.WallDismount;
            else if (IsWallJumping(bounds)) state = STATE.WallJump;
            else if (IsWallSticking(bounds)) state = STATE.WallSticking;
            else if (IsWallSliding(bounds) || IsWallStickingExit(bounds)) state = STATE.WallSliding;
            else if (IsVerticalMovement(bounds)) state = STATE.VerticalMovement;
            else if (IsFalling(bounds)) state = STATE.Fall;
            else if (IsJumping()) state = STATE.Jump;

        }

        #region Idle State

        private void StateIdleSwitch()
        {

            _velocity.x = 0;
            _velocity.y = 0;

            horizontalFriction = 0;
            verticalFriction = 0;

            currentAvailableJumps = maxAvailableJumps;

        }

        private void StateIdleLoop()
        {

            Loop();

        }

        private bool IsIdle(MovementBounds bounds)
        {

            return !state.Equals(STATE.Idle) &&
                (
                    position.y.NearlyEqual(bounds.bottom) && _velocity.x.NearlyEqual(0) ||
                    position.y.NearlyEqual(bounds.bottom) && position.x.NearlyEqual(bounds.left) ||
                    position.y.NearlyEqual(bounds.bottom) && position.x.NearlyEqual(bounds.right)
                );

        }

        #endregion

        #region Walking State

        private void StateWalkingSwitch()
        {

            throw new NotImplementedException();

        }

        private void StateWalkingLoop()
        {

            throw new NotImplementedException();

        }

        #endregion

        #region Running State

        private void StateRunningSwitch()
        {

            _velocity.y = 0;

            horizontalFriction = 0;
            verticalFriction = 0;

            currentAvailableJumps = maxAvailableJumps;

        }

        private void StateRunningLoop()
        {

            horizontalFriction = CalculateHorizontalFriction();

            _velocity.x = CalculateHorizontalVelocity(_velocity.x);

            Loop();

        }

        private bool IsRunning(MovementBounds bounds)
        {

            return !state.Equals(STATE.Running) && position.y.NearlyEqual(bounds.bottom) &&
                (Mathf.Abs(inputManager.inputHorizontal) > 0 || Mathf.Abs(_velocity.x) > 0) &&
                (
                    (!position.x.NearlyEqual(bounds.left) && inputManager.inputHorizontal <= 0) ||
                    (!position.x.NearlyEqual(bounds.right) && inputManager.inputHorizontal >= 0)
                );

        }

        #endregion

        #region Fall State

        private void StateFallSwitch()
        {

            _velocity.y = 0;

            horizontalFriction = 0;
            verticalFriction = 0;

            state = STATE.VerticalMovement;

        }

        private bool IsFalling(MovementBounds bounds)
        {

            return !state.Equals(STATE.VerticalMovement) && !state.Equals(STATE.WallSliding) && !state.Equals(STATE.WallSticking) &&
                !position.y.NearlyEqual(bounds.bottom) && _velocity.y <= 0 ||
                position.y.NearlyEqual(bounds.top);

        }

        #endregion

        #region Jump State

        private void StateJumpSwitch()
        {

            _velocity.y = highJumpSpeed;

            horizontalFriction = 0;
            verticalFriction = 0;

            currentAvailableJumps -= 1;

            state = STATE.VerticalMovement;

        }

        private bool IsJumping()
        {

            return inputManager.inputJumpDown && currentAvailableJumps >= 1;

        }

        #endregion

        #region Vertical Movement State

        private void StateVerticalMovementLoop()
        {

            _velocity.x = CalculateHorizontalVelocity(_velocity.x);
            _velocity.y = CalculateVerticalVelocity(_velocity.y);

            Loop();

        }

        private bool IsVerticalMovement(MovementBounds bounds)
        {

            return !state.Equals(STATE.VerticalMovement) && !_velocity.y.NearlyEqual(0) &&
                (
                    (!position.x.NearlyEqual(bounds.left) && inputManager.inputHorizontal < 0) ||
                    (!position.x.NearlyEqual(bounds.right) && inputManager.inputHorizontal > 0)
                );

        }

        #endregion

        #region Wall Sliding State

        private void StateWallSlidingSwitch()
        {

            _velocity.x = 0;

            horizontalFriction = 0;
            verticalFriction = 0;

            currentAvailableJumps = maxAvailableJumps;

        }

        private void StateWallSlidingLoop()
        {

            _velocity.y = CalculateVerticalVelocity(_velocity.y);

            Loop();

        }

        private bool IsWallSliding(MovementBounds bounds)
        {

            return !state.Equals(STATE.WallSliding) && !state.Equals(STATE.WallSticking) &&
                (position.x.NearlyEqual(bounds.left) || position.x.NearlyEqual(bounds.right)) &&
                !position.y.NearlyEqual(bounds.top) && !position.y.NearlyEqual(bounds.bottom);

        }

        #endregion

        #region Wall Sticking State

        private void StateWallStickingLoop()
        {

            verticalFriction = CalculateVerticalFriction();

            _velocity.y = (gravity.y + verticalFriction) * Time.deltaTime;

            Loop();

        }

        private bool IsWallSticking(MovementBounds bounds)
        {

            return toggleableStates.wallSticking && state.Equals(STATE.WallSliding) &&
                (
                    (position.x.NearlyEqual(bounds.left) && inputManager.inputHorizontal < 0) ||
                    (position.x.NearlyEqual(bounds.right) && inputManager.inputHorizontal > 0)
                );

        }

        private bool IsWallStickingExit(MovementBounds bounds)
        {

            return toggleableStates.wallSticking && state.Equals(STATE.WallSticking) &&
                (
                    (position.x.NearlyEqual(bounds.left) && inputManager.inputHorizontal >= 0) ||
                    (position.x.NearlyEqual(bounds.right) && inputManager.inputHorizontal <= 0)
                );

        }

        #endregion

        #region Wall Jumping State

        private void StateWallJumpingSwitch()
        {

            var bounds = CalculateMovementBounds();

            var horizontalDirection = 0;

            if (position.x.NearlyEqual(bounds.left)) horizontalDirection = 1;
            else if (position.x.NearlyEqual(bounds.right)) horizontalDirection = -1;

            _velocity.x = horizontalDirection * horizontalSpeed;

            state = STATE.Jump;

        }

        private bool IsWallJumping(MovementBounds bounds)
        {

            return toggleableStates.wallJumping && state.Equals(STATE.WallSliding) && inputManager.inputJumpDown &&
                (
                    (position.x.NearlyEqual(bounds.left) && inputManager.inputHorizontal >= 0) ||
                    (position.x.NearlyEqual(bounds.right) && inputManager.inputHorizontal <= 0)
                );

        }

        #endregion

        #region Wall Dismount State

        private void StateWallDismountSwitch()
        {

            _velocity.x = 0;

            horizontalFriction = 0;
            verticalFriction = 0;

            state = STATE.VerticalMovement;

        }

        private bool IsWallDismounting(MovementBounds bounds)
        {

            return state.Equals(STATE.WallSliding) &&
                (
                    (position.x.NearlyEqual(bounds.left) && inputManager.inputHorizontal > 0) ||
                    (position.x.NearlyEqual(bounds.right) && inputManager.inputHorizontal < 0)
                );

        }

        #endregion

        private float CalculateHorizontalVelocity(float velocityX)
        {

            if (Mathf.Abs(inputManager.inputHorizontal) > 0)
            {

                velocityX = Mathf.Lerp(velocityX, inputManager.inputHorizontal * horizontalSpeed, horizontalSpeed * Time.deltaTime);

            }

            if (velocity.x > 0)
            {

                velocityX = Mathf.Min(Mathf.Max(velocityX - Mathf.Max(airResistance, horizontalFriction), 0), horizontalSpeed);

            }
            else if (velocity.x < 0)
            {

                velocityX = Mathf.Max(Mathf.Min(velocityX + Mathf.Max(airResistance, horizontalFriction), 0), -horizontalSpeed);

            }

            return velocityX;

        }

        private float CalculateVerticalVelocity(float velocityY)
        {

            velocityY = Mathf.Max(velocityY + gravity.y * Time.deltaTime, gravity.y);

            return velocityY;

        }

        private Vector2 MoveStep(MovementBounds bounds)
        {

            var nextPosition = position;

            nextPosition += _velocity * Time.fixedDeltaTime;

            nextPosition.x = Mathf.Clamp(nextPosition.x, bounds.left, bounds.right);
            nextPosition.y = Mathf.Clamp(nextPosition.y, bounds.bottom, bounds.top);

            return nextPosition;

        }

        private MovementBounds CalculateMovementBounds()
        {

            var size = boxCollider.bounds.size;

            var horizontalSize = size - Vector3.up * (edgeCollidePreventionRatio * verticalExtents.y);
            var verticalSize = size - Vector3.right * (edgeCollidePreventionRatio * horizontalExtents.x);

            var hitLeftRay = Physics2D.BoxCastAll(position, horizontalSize, 0f, Vector2.left, size.x, layerMask.left)
                .FirstOrDefault(h => h.point.x < boxCollider.bounds.min.x);
            var hitRightRay = Physics2D.BoxCastAll(position, horizontalSize, 0f, Vector2.right, size.x, layerMask.right)
                .FirstOrDefault(h => h.point.x > boxCollider.bounds.max.x);
            var hitTopRay = Physics2D.BoxCastAll(position, verticalSize, 0f, Vector2.up, size.y, layerMask.top)
                .FirstOrDefault(h => h.point.y > boxCollider.bounds.max.y);
            var hitBottomRay = Physics2D.BoxCastAll(position, verticalSize, 0f, Vector2.down, size.y, layerMask.bottom)
                .FirstOrDefault(h => h.point.y < boxCollider.bounds.min.y);

            var bounds = new MovementBounds
            {
                left = hitLeftRay ? hitLeftRay.point.x + extents.x : Mathf.NegativeInfinity,
                right = hitRightRay ? hitRightRay.point.x - extents.x : Mathf.Infinity,
                top = hitTopRay ? hitTopRay.point.y - extents.y : Mathf.Infinity,
                bottom = hitBottomRay ? hitBottomRay.point.y + extents.y : Mathf.NegativeInfinity
            };

            return bounds;

        }

        private float CalculateHorizontalFriction()
        {

            var hitTopRay = Physics2D.CircleCast(position + verticalExtents, frictionRaycastRadius, Vector2.zero, 0, layerMask.top);
            var hitBottomRay = Physics2D.CircleCast(position - verticalExtents, frictionRaycastRadius, Vector2.zero, 0, layerMask.bottom);

            float friction = 0;

            if (hitTopRay) friction = hitTopRay.collider.friction;
            else if (hitBottomRay) friction = hitBottomRay.collider.friction;

            return friction;

        }

        private float CalculateVerticalFriction()
        {

            var hitLeftRay = Physics2D.CircleCast(position - horizontalExtents, frictionRaycastRadius, Vector2.zero, 0, layerMask.left);
            var hitRightRay = Physics2D.CircleCast(position + horizontalExtents, frictionRaycastRadius, Vector2.zero, 0, layerMask.right);

            float friction = 0;

            if (hitLeftRay) friction = hitLeftRay.collider.friction;
            else if (hitRightRay) friction = hitRightRay.collider.friction;

            return friction;

        }

        private void OnDrawGizmos()
        {

            if (displayDebugColliders)
            {

                boxCollider = gameObject.GetComponent<BoxCollider2D>();

                extents = boxCollider.bounds.extents;

                verticalExtents = new Vector2(0, extents.y);
                horizontalExtents = new Vector2(extents.x, 0);

                var size = boxCollider.bounds.size;

                var horizontalSize = size - Vector3.up * (edgeCollidePreventionRatio * verticalExtents.y);
                var verticalSize = size - Vector3.right * (edgeCollidePreventionRatio * horizontalExtents.x);

                position = gameObject.transform.position;

                var bounds = CalculateMovementBounds();

                Gizmos.color = Color.green;

                // Left
                Gizmos.DrawWireCube(position + Vector2.left * size.x, horizontalSize);
                Gizmos.DrawWireSphere(position - horizontalExtents, frictionRaycastRadius);
                Gizmos.DrawWireSphere(new Vector2(bounds.left - extents.x, position.y), 1);

                // Right
                Gizmos.DrawWireCube(position + Vector2.right * size.x, horizontalSize);
                Gizmos.DrawWireSphere(position + horizontalExtents, frictionRaycastRadius);
                Gizmos.DrawWireSphere(new Vector2(bounds.right + extents.x, position.y), 1);

                // Top
                Gizmos.DrawWireCube(position + Vector2.up * size.y, verticalSize);
                Gizmos.DrawWireSphere(position + verticalExtents, frictionRaycastRadius);
                Gizmos.DrawWireSphere(new Vector2(position.x, bounds.top + extents.y), 1);

                // Bottom
                Gizmos.DrawWireCube(position + Vector2.down * size.y, verticalSize);
                Gizmos.DrawWireSphere(position - verticalExtents, frictionRaycastRadius);
                Gizmos.DrawWireSphere(new Vector2(position.x, bounds.bottom - extents.y), 1);

            }

        }

    }

}
