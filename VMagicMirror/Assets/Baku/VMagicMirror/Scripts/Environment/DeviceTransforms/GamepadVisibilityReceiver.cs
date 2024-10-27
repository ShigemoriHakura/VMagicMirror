﻿using System;
using Deform;
using DG.Tweening;
using UniRx;
using UnityEngine;
using Zenject;

namespace Baku.VMagicMirror
{
    [RequireComponent(typeof(MagnetDeformer))]
    public class GamepadVisibilityReceiver : MonoBehaviour
    {
        //TODO: 非MonoBehaviour化できそう
        [Inject]
        public void Initialize(
            DeviceVisibilityManager deviceVisibilityManager,
            BodyMotionModeController bodyMotionModeController,
            HandIKIntegrator handIKIntegrator,
            DeformableCounter deformableCounter)
        {
            _deviceVisibilityManager = deviceVisibilityManager;
            _bodyMotionModeController = bodyMotionModeController;
            _handIkIntegrator = handIKIntegrator;
            _deformableCounter = deformableCounter;
        }

        private DeviceVisibilityManager _deviceVisibilityManager;
        private BodyMotionModeController _bodyMotionModeController;
        private HandIKIntegrator _handIkIntegrator;
        private DeformableCounter _deformableCounter;

        private MagnetDeformer _deformer = null;
        private Renderer[] _renderers = Array.Empty<Renderer>();

        public bool IsVisible { get; private set; }

        private void Start()
        {
            _deformer = GetComponent<MagnetDeformer>();
            _renderers = GetComponentsInChildren<Renderer>();

            //NOTE: 初期値で1回だけ発火してほしいので最初だけAsUnitObservableになっている
            Observable.Merge(
                _deviceVisibilityManager.GamepadVisible.AsUnitObservable(),
                _deviceVisibilityManager.HideUnusedDevices.AsUnitWithoutLatest(),
                _bodyMotionModeController.MotionMode.AsUnitWithoutLatest(),
                _bodyMotionModeController.GamepadMotionMode.AsUnitWithoutLatest(),
                _handIkIntegrator.LeftTargetType.AsUnitWithoutLatest(),
                _handIkIntegrator.RightTargetType.AsUnitWithoutLatest()
                )
                .Subscribe(_ => SetGamepadVisibility(IsGamepadVisible()))
                .AddTo(this);
        }

        private bool IsGamepadVisible()
        {
            // 設定の組み合わせに基づいたvisibilityがオフならその時点で非表示にしておく
            var settingBasedResult = 
                _deviceVisibilityManager.GamepadVisible.Value &&
                _bodyMotionModeController.MotionMode.Value is BodyMotionMode.Default &&
                _bodyMotionModeController.GamepadMotionMode.Value is GamepadMotionModes.Gamepad;

            if (!settingBasedResult)
            {
                return false;
            }

            if (!_deviceVisibilityManager.HideUnusedDevices.Value)
            {
                return true;
            }

            // この行まで到達した場合、設定に加えて動的な手IKの状態も考慮して表示/非表示を決める
            return
                _handIkIntegrator.LeftTargetType.Value is HandTargetType.Gamepad ||
                _handIkIntegrator.RightTargetType.Value is HandTargetType.Gamepad;
        }
        
        private void SetGamepadVisibility(bool visible)
        {
            if (visible == IsVisible)
            {
                return;
            }
            
            IsVisible = visible;
            DOTween
                .To(
                    () => _deformer.Factor, 
                    v => _deformer.Factor = v, 
                    visible ? 0.0f : 0.6f, 
                    0.5f)
                .SetEase(Ease.OutCubic)
                .OnStart(() =>
                {
                    _deformableCounter.Increment();
                    if (visible)
                    {
                        foreach (var r in _renderers)
                        {
                            r.enabled = true;
                        }
                    }
                })
                .OnComplete(() =>
                {
                    _deformableCounter.Decrement();
                    foreach (var r in _renderers)
                    {
                        r.enabled = IsVisible;
                    }
                });
        }
    }
}
