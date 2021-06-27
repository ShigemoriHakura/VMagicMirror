﻿using System;
using MediaPipe.HandPose;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace Baku.VMagicMirror.IK
{
    //TODO: リファクタ
    /// <summary> HandPoseBarracudaのトラッキングを手首IKと指曲げFKに割り当てるすごいやつだよ </summary>
    public class MPHand : MonoBehaviour
    {
        //この回数だけ連続でハンドトラッキングのスコアが閾値を超えたとき、トラッキング開始と見なす。
        //チャタリングのあるケースを厳し目に判定するために用いる
        private const int TrackingStartCount = 3;
        
        /// <summary> 手の検出の更新をサボる頻度オプション </summary>
        public enum FrameSkipStyles
        {
            //サボらず、毎フレームでLRを両方とも処理する。一番重たい
            BothOnSingleFrame,
            //サボらず、LとRを交互に処理し続ける
            LR,
            //Lを2フレームかけて処理後、Rを2フレームかけて処理、という形で交互に処理する
            LLRR,
        }

        [SerializeField] ResourceSet _resources = null;
        [SerializeField] bool _useAsyncReadback = true;
        [SerializeField] private FingerController _fingerController = null;

        //画像をタテに切っている関係で、画面中央に映った手 = だいたい肩の正面くらいに手がある、とみなしたい
        [SerializeField] private Vector3 rightHandOffset = new Vector3(0.25f, 0f, 0f);
        //wrist自体にはz座標が入っていないため、ちょっと手前に押し出しておく
        [SerializeField] private Vector3 commonAdditionalOffset = new Vector3(0f, 0f, 0.25f);
        //手と頭の距離にスケールをかけると、実際には頭の脇でちょこちょこ動かすだけのムーブを大きくできる
        [SerializeField] private Vector3 motionScale = Vector3.one;

        [Range(0f, 1f)] [SerializeField] private float scoreThreshold = 0.5f;
        [SerializeField] private FrameSkipStyles skipStyle = FrameSkipStyles.LLRR;
        [SerializeField] private float positionSmoothFactor = 12f;
        [SerializeField] private float rotationSmoothFactor = 12f;
        //NOTE: 手の立ち上がりが早すぎてキモくなることがあるので制限する。
        //この制限が適用されるとき、角度のLerpも同様に低速化しなければならないことに要注意。
        [SerializeField] private float positionMaxSpeed = 1f;
        [SerializeField] private Vector2Int resolution = new Vector2Int(640, 360);
        [Range(0.5f, 1f)] [SerializeField] private float textureWidthRateForOneHand = 0.6f;

        [Header("Tracking Loss Motion")]
        [SerializeField] private float lostCount = 1f;
        [SerializeField] private float lostCircleMotionLerpFactor = 3f;
        [SerializeField] private float lostEndMotionLerpFactor = 12f;
        [SerializeField] private float lostMotionDuration = 1.0f;
        
        [Header("Misc")]
        [SerializeField] private float dataSendInterval = 0.1f;
        [SerializeField] private ImageBaseHandLimitSetting handLimitSetting = null;

        [SerializeField] private RawImage webcamImage = null; 
        [SerializeField] private RawImage leftImage = null; 
        [SerializeField] private RawImage rightImage = null; 

        private Vector3 leftHandOffset => new Vector3(-rightHandOffset.x, rightHandOffset.y, rightHandOffset.z);

        #region 入力される参考情報 - 生の解析結果くらいまでのデータ

        //NOTE: Left/Rightという呼称がかなりややこしいので要注意。左右がぐるんぐるんします
        // - webCamのleft/right -> テクスチャを左右に分割しているだけ。
        //  - rightTextureにはユーザーの左手が映っています(ユーザーが手をクロスさせたりしない限りは)。
        // - pipelineおよびHandPointsのleft/right -> ユーザーの左手/右手を解析するためのパイプラインだよ、という意味。
        //  - leftPipelineはrightTextureを受け取ることで、ユーザーの左手の姿勢を解析します。
        // - HandStateのleft/right -> VRMの左手、右手のこと。
        //  - デフォルト設定では左右反転をするため、leftPipelineの結果をrightHandStateに適用します。
        
        private bool _hasWebCamTexture = false;
        private WebCamTexture _webCamTexture;
        private RenderTexture _leftTexture;
        private RenderTexture _rightTexture;

        private HandPipeline _leftPipeline = null;
        private HandPipeline _rightPipeline = null;

        //NOTE: スムージングしたくなりそうなので分けておく
        private readonly Vector3[] _leftHandPoints = new Vector3[HandPipeline.KeyPointCount];
        private readonly Vector3[] _rightHandPoints = new Vector3[HandPipeline.KeyPointCount];
        public Vector3[] LeftHandPoints => _leftHandPoints;

        private int _frameCount = 0;

        private bool _hasModel = false;
        private Transform _head = null;
        private Transform _leftUpperArm = null;
        private Transform _rightUpperArm = null;
        private Vector3 _defaultHeadPosition = Vector3.up;
        
        #endregion

        public bool ImageProcessEnabled { get; private set; } = false;
        
        public bool DisableHorizontalFlip { get; set; }

        public bool SendResult { get; set; }

        public AlwaysDownHandIkGenerator DownHand { get; set; }
        
        private readonly MPHandState _rightHandState = new MPHandState(ReactedHand.Right);
        public IHandIkState RightHandState => _rightHandState;
        private readonly MPHandState _leftHandState = new MPHandState(ReactedHand.Left);
        public IHandIkState LeftHandState => _leftHandState;

        private Vector3 _leftPosTarget;
        private Quaternion _leftRotTarget;
        private Vector3 _rightPosTarget;
        private Quaternion _rightRotTarget;

        private MPHandFinger _finger;
        private HandIkGeneratorDependency _dependency;
        private ImageBaseHandRotLimiter _limiter;
        
        private HandTrackingResultBuilder _resultBuilder;
        private float _resultSendCount = 0f;


        private int _leftTrackedCount = 0;
        private int _rightTrackedCount = 0;
        private float _leftLostCount = 0f;
        private float _rightLostCount = 0f;
        private float _leftScore = 0f;
        private float _rightScore = 0f;

        //NOTE: 複数フレームにわたって画像処理するシナリオについて、途中でGraphics.Blitするのを禁止するためのフラグ
        private bool _leftBlitBlock = false;
        private bool _rightBlitBlock = false;
        private float _leftTextureDt = -1f;
        private float _rightTextureDt = -1f;
        
        [Inject]
        public void Initialize(
            IVRMLoadable vrmLoadable,
            IMessageReceiver receiver,
            IMessageSender sender,
            FaceTracker faceTracker
            )
        {
            _resultBuilder = new HandTrackingResultBuilder(sender);
            
            vrmLoadable.VrmLoaded += info =>
            {
                _head = info.animator.GetBoneTransform(HumanBodyBones.Head);
                _leftUpperArm = info.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                _rightUpperArm = info.animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                _defaultHeadPosition = _head.position;
                _hasModel = true;
            };

            vrmLoadable.VrmDisposing += () =>
            {
                _hasModel = false;
                _leftUpperArm = null;
                _rightUpperArm = null;
                _head = null;
            };

            receiver.AssignCommandHandler(
                VmmCommands.EnableImageBasedHandTracking,
                c => ImageProcessEnabled = c.ToBoolean()
            );
            
            receiver.AssignCommandHandler(
                VmmCommands.DisableHandTrackingHorizontalFlip,
                c => DisableHorizontalFlip = c.ToBoolean()
            );
            receiver.AssignCommandHandler(
                VmmCommands.EnableSendHandTrackingResult,
                c => SendResult = c.ToBoolean()
                );

            _leftTexture = new RenderTexture((int) (resolution.x * textureWidthRateForOneHand), resolution.y, 0);
            _rightTexture = new RenderTexture((int) (resolution.x * textureWidthRateForOneHand), resolution.y, 0);
            faceTracker.WebCamTextureInitialized += SetWebcamTexture;
            faceTracker.WebCamTextureDisposed += DisposeWebCamTexture;

            _limiter = new ImageBaseHandRotLimiter(handLimitSetting);
        }

        public void SetupDependency(HandIkGeneratorDependency dependency)
        {
            _dependency = dependency;
        }
        
        private void SetWebcamTexture(WebCamTexture webCam)
        {
            DisposeWebCamTexture();
            _webCamTexture = webCam;
            _hasWebCamTexture = true;
        }

        private void DisposeWebCamTexture()
        {
            _hasWebCamTexture = false;
            _webCamTexture = null;
        }

        private void Start()
        {
            //NOTE: パイプラインは2つ要る。使いまわしできないので注意
            _leftPipeline = new HandPipeline(_resources);
            _rightPipeline = new HandPipeline(_resources);

            _leftPosTarget = _leftHandState.IKData.Position;
            _leftRotTarget = _leftHandState.IKData.Rotation;
            _rightPosTarget = _rightHandState.IKData.Position;
            _rightRotTarget = _rightHandState.IKData.Rotation;
            _finger = new MPHandFinger(_fingerController, _leftHandPoints, _rightHandPoints);
            _leftHandState.Finger = _finger;
            _rightHandState.Finger = _finger;

            _leftHandState.OnEnter += InitializeHandPosture;
            _rightHandState.OnEnter += InitializeHandPosture;
        }

        private void OnDestroy()
        {
            _leftPipeline.Dispose();
            _rightPipeline.Dispose();
        }

        private void Update()
        {
            if (!ImageProcessEnabled || !_hasWebCamTexture || !_hasModel)
            {
                _leftBlitBlock = false;
                _rightBlitBlock = false;
                _leftTrackedCount = 0;
                _rightTrackedCount = 0;
                return;
            }

            _leftPipeline.UseAsyncReadback = _useAsyncReadback;
            _rightPipeline.UseAsyncReadback = _useAsyncReadback;

            BlitTextures();
            CallHandUpdate();
            UpdateLostMotion();
            SendHandTrackingResult();
            
            //DEBUG
            // webcamImage.texture = _webCamTexture;
            // leftImage.texture = _leftTexture;
            // rightImage.texture = _rightTexture;

            LerpIKPose();
            
            void BlitTextures()
            {
                if (!_webCamTexture.didUpdateThisFrame)
                {
                    return;
                }

                //TODO: アス比によって絵が崩れる問題の対策。超重要
                var aspect1 = (float) _webCamTexture.width / _webCamTexture.height;
                var aspect2 = (float) resolution.x / resolution.y;
                var gap = aspect2 / aspect1;
                var vflip = _webCamTexture.videoVerticallyMirrored;
                var scale = new Vector2(gap, vflip ? -1 : 1);
                var offset = new Vector2((1 - gap) / 2, vflip ? 1 : 0);

                //L/R Image Blit
                var scaleToBlit = new Vector2(scale.x * textureWidthRateForOneHand, scale.y);
                if (!_leftBlitBlock)
                {
                    Graphics.Blit(_webCamTexture, _leftTexture, scaleToBlit, offset);
                }

                if (!_rightBlitBlock)
                {
                    Graphics.Blit(
                        _webCamTexture, _rightTexture, scaleToBlit,
                        new Vector2(offset.x + (1f - textureWidthRateForOneHand), offset.y)
                    );
                }
            }

            void CallHandUpdate()
            {
                _frameCount++;
                switch (skipStyle)
                {
                    case FrameSkipStyles.BothOnSingleFrame:
                        _frameCount = 0;
                        UpdateLeftHand();
                        UpdateRightHand();
                        break;
                    case FrameSkipStyles.LR:
                        if (_frameCount > 1)
                        {
                            UpdateRightHand();
                            _frameCount = 0;
                        }
                        else
                        {
                            UpdateLeftHand();
                        }

                        break;
                    case FrameSkipStyles.LLRR:
                        switch (_frameCount)
                        {
                            case 1:
                                UpdateLeftHandBefore();
                                break;
                            case 2:
                                UpdateLeftHandAfter();
                                break;
                            case 3:
                                UpdateRightHandBefore();
                                break;
                            case 4:
                            default:
                                UpdateRightHandAfter();
                                _frameCount = 0;
                                break;
                        }

                        break;
                }
            }

            void LerpIKPose()
            {
                var leftPos = Vector3.Lerp(
                    _leftHandState.IKData.Position, _leftPosTarget, positionSmoothFactor * Time.deltaTime
                );
                var leftDiff = leftPos - _leftHandState.IKData.Position;
                var leftSpeed = leftDiff.magnitude / Time.deltaTime;
                var leftSpeedRate = 1f;
                
                //速度が早すぎる場合は速度が律速になるよう低速化 + rotのLerpも弱める
                if (leftSpeed < positionMaxSpeed)
                {
                    _leftHandState.IKData.Position = leftPos;
                }
                else
                {
                    leftSpeedRate = positionMaxSpeed / leftSpeed;
                    _leftHandState.IKData.Position += leftDiff * leftSpeedRate;
                }
                
                _leftHandState.IKData.Rotation = Quaternion.Slerp(
                    _leftHandState.IKData.Rotation, 
                    _leftRotTarget, 
                    rotationSmoothFactor * Time.deltaTime * leftSpeedRate
                );
                
                
                var rightPos = Vector3.Lerp(
                    _rightHandState.IKData.Position, _rightPosTarget, positionSmoothFactor * Time.deltaTime
                );
                var rightDiff = rightPos - _rightHandState.IKData.Position;
                var rightSpeed = rightDiff.magnitude / Time.deltaTime;
                var rightSpeedRate = 1f;
                
                if (rightSpeed < positionMaxSpeed)
                {
                    _rightHandState.IKData.Position = rightPos;
                }
                else
                {
                    rightSpeedRate = positionMaxSpeed / rightSpeed;
                    _rightHandState.IKData.Position += rightDiff * rightSpeedRate;
                }
                
                _rightHandState.IKData.Rotation = Quaternion.Slerp(
                    _rightHandState.IKData.Rotation, 
                    _rightRotTarget, 
                    rotationSmoothFactor * Time.deltaTime * rightSpeedRate
                );
                
            }
        }

        private void UpdateLeftHandBefore()
        {
            //NOTE: 左手はwebcam画像の右側に映っているので、右テクスチャを固定しつつ見に行く
            _rightBlitBlock = true;
            _rightTextureDt = Time.deltaTime;
            _leftPipeline.DetectPalm(_rightTexture, _rightTextureDt);
        }

        private void UpdateLeftHandAfter()
        {
            //NOTE: 左手はwebcam画像の右側に映っているので、前半と同じく右テクスチャを見に行く
            _rightBlitBlock = false;
            _leftPipeline.CalculateLandmarks(_rightTexture, _rightTextureDt);

            //解析が終わった = いろいろ見ていく。ただしスコアが低いのは無視
            var pipeline = _leftPipeline;
            _leftScore = pipeline.Score;
            if (_leftScore < scoreThreshold)
            {
                _leftTrackedCount = 0;
                return;
            }

            _leftTrackedCount++;
            if (_leftTrackedCount < TrackingStartCount)
            {
                return;
            }
            
            _leftLostCount = 0f;
            
            for (var i = 0; i < HandPipeline.KeyPointCount; i++)
            {
                //XとZをひっくり返すと鏡像的なアレが直る
                var p = pipeline.GetKeyPoint(i);
                _leftHandPoints[i] = new Vector3(-p.x, p.y, -p.z);
            }

            
            var rotInfo = CalculateLeftHandRotation();
            
            if (DisableHorizontalFlip)
            {
                _leftPosTarget = 
                    _defaultHeadPosition + commonAdditionalOffset + 
                    MathUtil.Mul(motionScale, leftHandOffset + _leftHandPoints[0]);
                _leftRotTarget = _limiter.CalculateLeftHandRotation(
                    rotInfo.Item1 * Quaternion.AngleAxis(90f, Vector3.up)
                );
            }
            else
            {
                var p = _leftHandPoints[0];
                p.x = -p.x;
                _rightPosTarget =
                    _defaultHeadPosition + commonAdditionalOffset + 
                    MathUtil.Mul(motionScale, rightHandOffset + p);
                var rightRot = rotInfo.Item1;
                rightRot.y *= -1f;
                rightRot.z *= -1f;

                _rightRotTarget = _limiter.CalculateRightHandRotation(
                    rightRot * Quaternion.AngleAxis(-90f, Vector3.up)
                );
            }
            
            if (DisableHorizontalFlip)
            {
                _leftHandState.RaiseRequestToUse();
            }
            else
            {
                _rightHandState.RaiseRequestToUse();
            }

            //NOTE: 状態をチェックすることにより、「つねに手下げモード」時とかに指が動いてしまうのを防ぐ
            if ((DisableHorizontalFlip && _dependency.Config.LeftTarget.Value == HandTargetType.ImageBaseHand) ||
                (!DisableHorizontalFlip && _dependency.Config.RightTarget.Value == HandTargetType.ImageBaseHand)
                )
            {
                _finger.UpdateLeft(rotInfo.Item2, rotInfo.Item3);
                _finger.ApplyLeftFingersDataToModel(DisableHorizontalFlip);
            }
        }

        private void UpdateLeftHand()
        {
            UpdateLeftHandBefore();
            UpdateLeftHandAfter();
        }

        private void UpdateRightHandBefore()
        {
            _leftBlitBlock = true;
            _leftTextureDt = Time.deltaTime;
            _rightPipeline.DetectPalm(_leftTexture, _leftTextureDt);
        }

        private void UpdateRightHandAfter()
        {
            _leftBlitBlock = false;
            _rightPipeline.CalculateLandmarks(_leftTexture, _leftTextureDt);
           
            var pipeline = _rightPipeline;
            _rightScore = pipeline.Score;
            if (_rightScore < scoreThreshold)
            {
                _rightTrackedCount = 0;
                return;
            }

            _rightTrackedCount++;
            if (_rightTrackedCount < TrackingStartCount)
            {
                return;
            }
            _rightLostCount = 0f;
            
            for (var i = 0; i < HandPipeline.KeyPointCount; i++)
            {
                var p = pipeline.GetKeyPoint(i);
                _rightHandPoints[i] = new Vector3(-p.x, p.y, -p.z);
            }

            var rotInfo = CalculateRightHandRotation();

            if (DisableHorizontalFlip)
            {
                _rightPosTarget =
                    _defaultHeadPosition + commonAdditionalOffset + 
                    MathUtil.Mul(motionScale, rightHandOffset + _rightHandPoints[0]);
                _rightRotTarget = _limiter.CalculateRightHandRotation(
                    rotInfo.Item1 * Quaternion.AngleAxis(-90f, Vector3.up)
                );
            }
            else
            {
                var p = _rightHandPoints[0];
                p.x = -p.x;
                _leftPosTarget = 
                    _defaultHeadPosition + commonAdditionalOffset + 
                    MathUtil.Mul(motionScale, leftHandOffset + p);
                var leftRot = rotInfo.Item1;
                leftRot.y *= -1f;
                leftRot.z *= -1f;
                _leftRotTarget = _limiter.CalculateLeftHandRotation(
                    leftRot * Quaternion.AngleAxis(90f, Vector3.up)
                );
            }

            if (DisableHorizontalFlip)
            {
                _rightHandState.RaiseRequestToUse();
            }
            else
            {
                _leftHandState.RaiseRequestToUse();
            }
            
            //NOTE: 状態をチェックすることにより、「つねに手下げモード」時とかに指が動いてしまうのを防ぐ
            if ((DisableHorizontalFlip && _dependency.Config.RightTarget.Value == HandTargetType.ImageBaseHand) ||
                (!DisableHorizontalFlip && _dependency.Config.LeftTarget.Value == HandTargetType.ImageBaseHand)
                )
            {
                _finger.UpdateRight(rotInfo.Item2, rotInfo.Item3);
                _finger.ApplyRightFingersDataToModel(DisableHorizontalFlip);
            }
        }

        private void UpdateRightHand()
        {
            UpdateRightHandBefore();
            UpdateRightHandAfter();
        }

        private void UpdateLostMotion()
        {
            _leftLostCount += Time.deltaTime;
            _rightLostCount += Time.deltaTime;

            var circleMotionFactor = lostCircleMotionLerpFactor * Time.deltaTime;
            var endFactor = lostEndMotionLerpFactor * Time.deltaTime;

            if ((_leftLostCount > lostCount && DisableHorizontalFlip) ||
                (_rightLostCount > lostCount && !DisableHorizontalFlip))
            {
                var lostOver = DisableHorizontalFlip
                    ? _leftLostCount - lostCount
                    : _rightLostCount - lostCount;

                
                //NOTE: トラッキングロスモーションは2フェーズに分かれる。
                // 1. ロス直後: 「手を体の横に開いて下ろす」という円軌道寄りに手を持っていく
                // 2. それ以降: 単に手降ろし状態に持っていく
                if (lostOver < lostMotionDuration)
                {
                    var rate = lostOver / lostMotionDuration;
                    var factor = Mathf.Lerp(circleMotionFactor, endFactor, rate);
                    var (pos, rot) = GetLostLeftHandPose(rate);
                    _leftPosTarget = Vector3.Lerp(_leftPosTarget, pos, factor);
                    _leftRotTarget = Quaternion.Slerp(_leftRotTarget, rot, factor);
                }
                else
                {
                    _leftPosTarget = Vector3.Lerp(_leftPosTarget, DownHand.LeftHand.Position, endFactor);
                    _leftRotTarget = Quaternion.Slerp(_leftRotTarget, DownHand.LeftHand.Rotation, endFactor);
                }
            }
            
            if ((_rightLostCount > lostCount && DisableHorizontalFlip) ||
                (_leftLostCount > lostCount && !DisableHorizontalFlip))
            {
                var lostOver = DisableHorizontalFlip
                    ? _rightLostCount - lostCount
                    : _leftLostCount - lostCount;

                //NOTE: トラッキングロスモーションは2フェーズに分かれる。
                // 1. ロス直後: 「手を体の横に開いて下ろす」という円軌道寄りに手を持っていく
                // 2. それ以降: 単に手降ろし状態に持っていく
                if (lostOver < lostMotionDuration)
                {
                    var rate = lostOver / lostMotionDuration;
                    var factor = Mathf.Lerp(circleMotionFactor, endFactor, rate);
                    var (pos, rot) = GetLostRightHandPose(rate);
                    _rightPosTarget = Vector3.Lerp(_rightPosTarget, pos, factor);
                    _rightRotTarget = Quaternion.Slerp(_rightRotTarget, rot, factor);
                }
                else
                {
                    _rightPosTarget = Vector3.Lerp(_rightPosTarget, DownHand.RightHand.Position, endFactor);
                    _rightRotTarget = Quaternion.Slerp(_rightRotTarget, DownHand.RightHand.Rotation, endFactor);
                }
            }
        }

        private void SendHandTrackingResult()
        {
            if (!SendResult)
            {
                _resultSendCount = 0f;
                return;
            }

            _resultSendCount += Time.deltaTime;
            if (_resultSendCount < dataSendInterval)
            {
                return;
            }
            _resultSendCount -= dataSendInterval;
            
            _resultBuilder.SendResult(
                _leftScore >= scoreThreshold,
                _leftScore,
                _leftHandPoints,
                _rightScore >= scoreThreshold,
                _rightScore,
                _rightHandPoints
                );
        }
        
        //左手の取るべきワールド回転に関連して、回転、手の正面方向ベクトル、手のひらと垂直なベクトルの3つを計算します。
        private (Quaternion, Vector3, Vector3) CalculateLeftHandRotation()
        {
            var wristForward = (_leftHandPoints[9] - _leftHandPoints[0]).normalized;
            //NOTE: 右手と逆の順にすることに注意
            var wristUp = Vector3.Cross(
                _leftHandPoints[17] - _leftHandPoints[0],
                wristForward
            ).normalized;

            var rot = Quaternion.LookRotation(wristForward, wristUp);
            return (rot, wristForward, wristUp);
        }
    
        //右手の取るべきワールド回転に関連して、回転、手の正面方向ベクトル、手のひらと垂直なベクトルの3つを計算します。
        private (Quaternion, Vector3, Vector3) CalculateRightHandRotation()
        {
            //正面 = 中指方向
            var wristForward = (_rightHandPoints[9] - _rightHandPoints[0]).normalized;
            //手首と垂直 = 人差し指あるいは中指方向、および小指で外積を取ると手の甲方向のベクトルが得られる
            var wristUp = Vector3.Cross(
                wristForward, 
                _rightHandPoints[17] - _rightHandPoints[0]
            ).normalized;

            //局所座標の余ってるベクトル = 右手の親指付け根から小指付け根方向のベクトル
            // var right = Vector3.Cross(up, forward)

            var rot = Quaternion.LookRotation(wristForward, wristUp);
            return (rot, wristForward, wristUp);
        }
        
        // 他のIKからこのIKに遷移した瞬間に呼び出すことで、直前のIKの姿勢をコピーして遷移をなめらかにする
        private void InitializeHandPosture(ReactedHand hand, IIKData src)
        {
            if (src == null)
            {
                return;
            }
            
            switch (hand)
            {
                case ReactedHand.Left:
                    _leftHandState.Position = src.Position;
                    _leftHandState.Rotation = src.Rotation;
                    break;
                case ReactedHand.Right:
                    _rightHandState.Position = src.Position;
                    _rightHandState.Rotation = src.Rotation;
                    break;
            }
        }

        private (Vector3, Quaternion) GetLostLeftHandPose(float rate)
        {
            var upperArmPos = _leftUpperArm.position;
            var diff = DownHand.LeftHand.Position - upperArmPos;
            //z成分はそのままに、真横に手を置いたベクトルを作る
            var diffHorizontal = Vector3.left * new Vector2(diff.x, diff.y).magnitude;

            //NOTE: -70degくらいになるよう符号を変換 + [-180, 180]で範囲保証
            var angle = Mathf.Repeat(Mathf.Rad2Deg * Mathf.Atan2(diff.y, -diff.x) + 180f, 360f) - 180f;
            //適用時は+方向に曲げたいのでこんな感じ
            var resultPos = upperArmPos + Quaternion.AngleAxis(-angle * rate, Vector3.forward) * diffHorizontal;
            //NOTE: Slerpでも書けるが、こっちのほうが計算的にラクなはず
            var resultRot = Quaternion.AngleAxis(-angle * rate, Vector3.forward);

            return (resultPos, resultRot);
        }
        
        private (Vector3, Quaternion) GetLostRightHandPose(float rate)
        {
            var upperArmPos = _rightUpperArm.position;
            var diff = DownHand.RightHand.Position - upperArmPos;
            var diffHorizontal = Vector3.right * new Vector2(diff.x, diff.y).magnitude;

            //NOTE: -70degくらいのはず
            var angle = Mathf.Repeat(Mathf.Rad2Deg * Mathf.Atan2(diff.y, diff.x) + 180f, 360f) - 180f;
            var resultPos = upperArmPos + Quaternion.AngleAxis(angle * rate, Vector3.forward) * diffHorizontal;
            var resultRot = Quaternion.AngleAxis(angle * rate, Vector3.forward);
            
            return (resultPos, resultRot);
        }
        
        private class MPHandState : IHandIkState
        {
            public MPHandState(ReactedHand hand)
            {
                Hand = hand;
            }
            
            public MPHandFinger Finger { get; set; }
            public bool DisableHorizontalFlip { get; set; }
            
            public IKDataRecord IKData { get; } = new IKDataRecord();

            public Vector3 Position
            {
                get => IKData.Position;
                set => IKData.Position = value;
            } 
            
            public Quaternion Rotation
            {
                get => IKData.Rotation;
                set => IKData.Rotation = value;
            }

            public ReactedHand Hand { get; }
            public HandTargetType TargetType => HandTargetType.ImageBaseHand;

            public void RaiseRequestToUse() => RequestToUse?.Invoke(this);
            public event Action<IHandIkState> RequestToUse;

            public event Action<ReactedHand, IHandIkState> OnEnter;

            public void Enter(IHandIkState prevState) => OnEnter?.Invoke(Hand, prevState);

            public void Quit(IHandIkState nextState)
            {
                if (Hand == ReactedHand.Left)
                {
                    Finger?.ReleaseLeftHand();
                }
                else
                {
                    Finger?.ReleaseRightHand();
                }
            }
        }
    }
}
