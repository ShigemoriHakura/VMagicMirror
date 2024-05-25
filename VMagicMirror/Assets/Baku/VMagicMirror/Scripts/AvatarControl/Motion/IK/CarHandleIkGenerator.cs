using System;
using UniRx;
using UnityEngine;

namespace Baku.VMagicMirror.IK
{
    /// <summary>
    /// このクラスができる計算
    /// - 角度に対して両手のIK位置を決める
    /// - とくに、途中の持ち替え操作に対応している
    /// </summary>
    
    public class CarHandleIkGenerator : HandIkGeneratorBase
    {
        // [SerializeField] private Transform leftHandIkTarget = null;
        // [SerializeField] private Transform rightHandIkTarget = null;
        // [SerializeField] private Transform bodyRotationTarget = null;
        //
        // [SerializeField] private Transform centerOfHandle = null;
        // [SerializeField] private Transform handleRotationVisual = null;

        //TODO: Dependencyから受け取る値たち
        private Transform centerOfHandle;
        private float HandleRadius = 0.4f;
        private AnimationCurve angleToHeadYawRateCurve;


        private const float bodyRotationAngleLimit = 3f;
        private const float HandleGripChangeDuration = 0.3f;


        private const float AngleUpperOffset = 30f;
        private const float AngleDownDiff = 50f;
        private const float AngleUpDiff = 120f;
        private const float MaxAngle = 540f;
        private float currentAngle;

        private readonly CarHandleProvider _provider;
        
        public CarHandleIkGenerator(
            HandIkGeneratorDependency dependency,
            CarHandleProvider provider
            ) : base(dependency)
        {
            _provider = provider;

            _leftHandState = new HandleHandState(
                this, ReactedHand.Left, 150f, 150f, 60f
                );
            _rightHandState = new HandleHandState(
                this, ReactedHand.Right, 30f, 60f, 150f
                );
            
            //該当モードでスティックに触ると両手がハンドル用IKになる: 片手ずつでもいいかもだが
            dependency.Events.MoveLeftGamepadStick += v =>
            {
                if (dependency.Config.IsAlwaysHandDown.Value || 
                    dependency.Config.GamepadMotionMode.Value != GamepadMotionModes.CarController)
                {
                    return;
                }

                _leftHandState.RaiseRequest();
                _rightHandState.RaiseRequest();
            };

            dependency.Events.MoveRightGamepadStick += v =>
            {
                if (dependency.Config.IsAlwaysHandDown.Value || 
                    dependency.Config.GamepadMotionMode.Value != GamepadMotionModes.CarController)
                {
                    return;
                }

                _leftHandState.RaiseRequest();
                _rightHandState.RaiseRequest();
            };
        }
        
        private readonly HandleHandState _leftHandState;
        public override IHandIkState LeftHandState => _leftHandState;
        private readonly HandleHandState _rightHandState;
        public override IHandIkState RightHandState => _rightHandState;

        //下記の公開値はHandIkじゃないけど、HandIkの適用中に値が効く

        private readonly ReactiveProperty<float> _bodyRotationRate = new(0f);
        //NOTE: 1になると最大限まで左に傾く
        public IReadOnlyReactiveProperty<float> BodyRotationRate => _bodyRotationRate;

        private readonly ReactiveProperty<float> _eyeRotationRate = new(0f);
        public IReadOnlyReactiveProperty<float> EyeRotationRate => _eyeRotationRate;

        private readonly ReactiveProperty<float> _headYawRotationRate = new(0);
        public IReadOnlyReactiveProperty<float> HeadYawRotationRate => _headYawRotationRate;

        private static float Sigmoid(float value, float factor, float pow)
        {
            return 2f / (1 + Mathf.Pow(pow, -value / factor)) - 1f;
        }

        private static float GetBodyRotationRate(float angle) => Sigmoid(angle, 180f, 4);

        private float GetHeadRotationRate(float angle)
        {
            //NOTE: 0~90degあたりにほぼ不感になるエリアが欲しいのでカーブを使ってます
            var rate = Mathf.Clamp01(Mathf.Abs(angle / MaxAngle));
            return Mathf.Sign(angle) * angleToHeadYawRateCurve.Evaluate(rate);
        }

        private float GetEyeRotationRate(float angle) => Sigmoid(angle, 85f, 4);
        
        public override void Update()
        {
            if (centerOfHandle == null)
            {
                return;
            }

            //NOTE: 定数化したら更新を省けるやつがあるので、コード自体を省くこと！
            _leftHandState.HandleTransform = centerOfHandle;
            _leftHandState.HandleRadius = HandleRadius;
            _leftHandState.GripChangeMoveDuration = HandleGripChangeDuration;

            _rightHandState.HandleTransform = centerOfHandle;
            _rightHandState.HandleRadius = HandleRadius;
            _rightHandState.GripChangeMoveDuration = HandleGripChangeDuration;

            _leftHandState.DefaultAngle = 180f - AngleUpperOffset;
            _leftHandState.AngleMinusDiff = AngleUpDiff;
            _leftHandState.AnglePlusDiff = AngleDownDiff;

            _rightHandState.DefaultAngle = AngleUpperOffset;
            _rightHandState.AngleMinusDiff = AngleDownDiff;
            _rightHandState.AnglePlusDiff = AngleUpDiff;
           
            
            //NOTE: スティックの右方向が正にする場合、 `angle = -stick.x` みたいな関係になりうるので注意 

            var dt = Time.deltaTime;
            _leftHandState.HandleAngle = currentAngle;
            _rightHandState.HandleAngle = currentAngle;
            _leftHandState.Update(dt);
            _rightHandState.Update(dt);
            _bodyRotationRate.Value = GetBodyRotationRate(currentAngle);
            _headYawRotationRate.Value = GetHeadRotationRate(currentAngle);
            _eyeRotationRate.Value = GetEyeRotationRate(currentAngle);

            //ApplyCurrentPoses();
        }

        //NOTE: ここは本来別のクラスでやってほしい
        private void ApplyCurrentPoses()
        {
            // handleRotationVisual.localRotation = Quaternion.AngleAxis(currentAngle, Vector3.forward);
            //
            // var leftPose = _leftHandState.CurrentPose.Value;
            // var rightPose = _rightHandState.CurrentPose.Value;
            // leftHandIkTarget.SetPositionAndRotation(_leftHandState.CurrentPose.Value.position, leftPose.rotation);
            // rightHandIkTarget.SetPositionAndRotation(rightPose.position, rightPose.rotation);
            //
            // if (bodyRotationTarget != null)
            // {
            //     bodyRotationTarget.localRotation = 
            //         Quaternion.AngleAxis(bodyRotationAngleLimit * BodyRotationRate.Value, Vector3.forward);
            // }
        }

        class HandleHandState : IHandIkState
        {
            // NOTE: 角度は真右を始点、反時計周りを正としてdegreeで指定する(例外は都度書く)

            public HandleHandState(
                CarHandleIkGenerator parent, ReactedHand hand, 
                float defaultAngle, float angleMinusDiff, float anglePlusDiff)
            {
                _parent = parent;
                Hand = hand;
                DefaultAngle = defaultAngle;
                AngleMinusDiff = angleMinusDiff;
                AnglePlusDiff = anglePlusDiff;
            }

            
            private readonly CarHandleIkGenerator _parent;
            
            /// <summary> ハンドルが0度のときに掴んでる角度 </summary>
            public float DefaultAngle { get; set; }
            
            /// <summary> 掴み角度デフォルトの角度からこの値だけマイナスすると、それ以上は握っていられない…という値を正の値で指定する </summary>
            public float AngleMinusDiff { get; set; }
            
            /// <summary> デフォルトの角度からこの値だけプラスすると、それ以上は握っていられない…という値を正の値で指定する </summary>
            public float AnglePlusDiff { get; set; }

            /// <summary>
            /// ゲームパッドのスティック等で定まる、ハンドルが回転しているべき角度。正負や、 > +360 の値などに意味があるので注意
            /// ※このクラス自体はハンドルの角度を純粋なinputにする(手が追いつかないから回せない、とかはない)ことに注意！
            /// </summary>
            public float HandleAngle { get; set; }

            //入力系の値で、アバターや空間編集するときだけ変化するやつ
            public float HandleRadius { get; set; } = 0.2f;
            public Transform HandleTransform { get; set; }

            public float NonGripZOffset { get; set; } = -.1f;
            
            //出力系で、公開する値
            private readonly ReactiveProperty<Pose> _currentPose = new (Pose.identity);
            public IReadOnlyReactiveProperty<Pose> CurrentPose => _currentPose;

            private readonly ReactiveProperty<bool> _isGripping = new(false);
            public IReadOnlyReactiveProperty<bool> IsGripping => _isGripping;

            //NOTE: 正であることが必須
            public float GripChangeMoveDuration { get; set; } = 0.3f;

            //NOTE: ハンドルの持ち替え回数を示す値で、1回握り直すたびに増える
            private int _gripChangeCount;
            
            private float _prevHandleAngle;
            private float _gripChangeMotionCount = 0;
            private Pose _gripMotionStartPose = Pose.identity;
            
            public void Update(float deltaTime)
            {
                if (HandleTransform == null)
                {
                    return;
                }

                var (gripChangeCount, targetAngle) = CalculateHandleTarget();
                if (gripChangeCount != _gripChangeCount)
                {
                    //NOTE: _isGrippingがfalseのときにここを通過し直すとモーションのdurationを数え直して握りモーションをやり直す。
                    //ここを何度も通ると永遠にハンドルを握れなくなるが、見た目がよほどアレじゃなければ許容する
                    _gripChangeMotionCount = 0f;
                    _isGripping.Value = false;
                    _gripChangeCount = gripChangeCount;
                    _gripMotionStartPose = _currentPose.Value;
                }

                if (_isGripping.Value)
                {
                    _currentPose.Value = GetHandleGrippedPose(targetAngle);
                }
                else
                {
                    _currentPose.Value = GetHandleNonGrippedPose(
                        _gripMotionStartPose,
                        GetHandleGrippedPose(targetAngle),
                        _gripChangeMotionCount / GripChangeMoveDuration
                    );

                    _gripChangeMotionCount += deltaTime;
                    if (_gripChangeMotionCount >= GripChangeMoveDuration)
                    {
                        _isGripping.Value = true;
                    }
                }
            }

            private (int gripChangeCount, float angle) CalculateHandleTarget()
            {
                // _innerTargetAngleの決まり方
                // - 片手だけで (Default - MinDiff) ~ (Default + MaxDiff) の幅でハンドルを回してHandleAngleまで回転する、
                //   という冪等な操作を想定して計算する。
                var handleAngle = HandleAngle;

                //ほぼ正位置のハンドル
                if (handleAngle > -AngleMinusDiff && handleAngle < AnglePlusDiff)
                {
                    return (0, HandleAngle + DefaultAngle);
                }

                var angleWidth = AnglePlusDiff + AngleMinusDiff;
                
                //左にぐるぐる回ってるハンドル
                if (handleAngle > AnglePlusDiff)
                {
                    var gripChangeCount = Mathf.FloorToInt((HandleAngle - AnglePlusDiff) / angleWidth) + 1;
                    var angle = DefaultAngle - AngleMinusDiff + Mathf.Repeat(HandleAngle - AnglePlusDiff, angleWidth);
                    return (gripChangeCount, angle);
                }

                //右にぐるぐる回ってるハンドル
                else
                {
                    var gripChangeCount = Mathf.FloorToInt((-HandleAngle - AngleMinusDiff) / angleWidth) - 1;
                    var angle = DefaultAngle + AnglePlusDiff - Mathf.Repeat(-HandleAngle - AngleMinusDiff, angleWidth);
                    return (gripChangeCount, angle);
                }
            }
            
            private Pose GetHandleGrippedPose(float angle)
            {
                var t = HandleTransform;
                
                var rotation = t.rotation * Quaternion.AngleAxis(angle, Vector3.forward);
                var position =
                    t.position +
                    Quaternion.AngleAxis(angle, t.forward) * (HandleRadius * t.right);
    
                return new Pose(position, rotation);
            }

            private Pose GetHandleNonGrippedPose(Pose startPose, Pose endPose, float rate)
            {
                //ポイント:
                // - 持ち替えるときにハンドルのちょっと手前側にIKが来る
                // - rateは適当にsmoothしておく

                rate = Mathf.SmoothStep(0f, 1f, rate);
                
                var zOffsetRate = 1f;
                if (rate < 0.3f)
                {
                    zOffsetRate = rate / 0.3f;
                }
                else if (rate > 0.7f)
                {
                    zOffsetRate = (1 - rate) / 0.3f;
                }
                var positionOffset = (zOffsetRate * NonGripZOffset) * HandleTransform.forward;

                //TODO: Quaternionコレでキレイにならないのでは？
                return new Pose(
                    Vector3.Lerp(startPose.position, endPose.position, rate) + positionOffset,
                    Quaternion.Slerp(startPose.rotation, endPose.rotation, rate)
                );
            }
            
            #region IHandIkState
            
            public bool SkipEnterIkBlend => false;
            
            public Vector3 Position => _currentPose.Value.position;
            public Quaternion Rotation => _currentPose.Value.rotation;
            
            public ReactedHand Hand { get; }
            public HandTargetType TargetType => HandTargetType.CarHandle;

            public event Action<IHandIkState> RequestToUse;

            public void RaiseRequest() => RequestToUse?.Invoke(this);

            //NOTE: 横着でGrip表現にGamepadFingerを使っているが、たぶんバランスが悪いはずなので別で用意して欲しい
            public void Enter(IHandIkState prevState)
            {
                if (Hand == ReactedHand.Left)
                {
                    _parent.Dependency.Reactions.GamepadFinger.GripLeftHand();
                }
                else
                {
                    _parent.Dependency.Reactions.GamepadFinger.GripRightHand();
                }
            }

            public void Quit(IHandIkState nextState)
            {
                if (Hand == ReactedHand.Left)
                {
                    _parent.Dependency.Reactions.GamepadFinger.ReleaseLeftHand();
                }
                else
                {
                    _parent.Dependency.Reactions.GamepadFinger.ReleaseRightHand();
                }
            }
            
            #endregion
        }
        
        
        // private class CarHandleHandIkState : IHandIkState
        // {
        //     public CarHandleHandIkState(CarHandleIkGenerator parent, ReactedHand hand)
        //     {
        //         _parent = parent;
        //         Hand = hand;
        //         _data = hand == ReactedHand.Right ? _parent._rightHand : _parent._leftHand;
        //     }
        //     private readonly CarHandleIkGenerator _parent;
        //     private readonly IIKData _data;
        //
        //     public bool SkipEnterIkBlend => false;
        //     public void RaiseRequest() => RequestToUse?.Invoke(this);
        //
        //     public Vector3 Position => _data.Position;
        //     public Quaternion Rotation => _data.Rotation;
        //     public ReactedHand Hand { get; }
        //     public HandTargetType TargetType => HandTargetType.CarHandle;
        //     public event Action<IHandIkState> RequestToUse;
        //     
        //     public void Enter(IHandIkState prevState)
        //     {
        //         if (Hand == ReactedHand.Left)
        //         {
        //             _parent.Dependency.Reactions.GamepadFinger.GripLeftHand();
        //         }
        //         else
        //         {
        //             _parent.Dependency.Reactions.GamepadFinger.GripRightHand();
        //         }
        //     }
        //
        //     public void Quit(IHandIkState nextState)
        //     {
        //         if (Hand == ReactedHand.Left)
        //         {
        //             _parent.Dependency.Reactions.GamepadFinger.ReleaseLeftHand();
        //         }
        //         else
        //         {
        //             _parent.Dependency.Reactions.GamepadFinger.ReleaseRightHand();
        //         }
        //     }
        // }

    }
}
