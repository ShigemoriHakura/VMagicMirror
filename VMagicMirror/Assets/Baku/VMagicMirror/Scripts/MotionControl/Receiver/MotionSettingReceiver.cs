﻿using UnityEngine;
using Zenject;
using Baku.VMagicMirror.InterProcess;

namespace Baku.VMagicMirror
{
    //TODO: このクラスは設計に問題抱えてそう…なんだけど一瞬で治る問題ではないのが何とも。

    /// <summary> モーション関係で、操作ではなく設定値を受け取るレシーバクラス </summary>
    public class MotionSettingReceiver : MonoBehaviour
    {
        //Word to Motionの専用入力に使うデバイスを指定する定数値
//        private const int DeviceTypeNone = -1;
        private const int DeviceTypeKeyboardWord = 0;
        private const int DeviceTypeGamepad = 1;
        private const int DeviceTypeKeyboardTenKey = 2;
        private const int DeviceTypeMidiController = 3;
        
        [SerializeField] private GamepadBasedBodyLean gamePadBasedBodyLean = null;
        [SerializeField] private SmallGamepadHandIKGenerator smallGamepadHandIk = null;
        [SerializeField] private HandIKIntegrator handIkIntegrator = null;
        [SerializeField] private HeadIkIntegrator headIkIntegrator = null;
        
        private StatefulXinputGamePad _gamePad = null;

        [Inject]
        public void Initialize(IMessageReceiver receiver, StatefulXinputGamePad gamePad)
        {
            _gamePad = gamePad;
            receiver.AssignCommandHandler(
                MessageCommandNames.EnableHidArmMotion,
                message => handIkIntegrator.EnableHidArmMotion = message.ToBoolean()
                );
            receiver.AssignCommandHandler(
                MessageCommandNames.LengthFromWristToPalm,
                message => SetLengthFromWristToPalm(message.ParseAsCentimeter())
                );
            receiver.AssignCommandHandler(
                MessageCommandNames.LengthFromWristToTip,
                message => SetLengthFromWristToTip(message.ParseAsCentimeter())
                );
            receiver.AssignCommandHandler(
                MessageCommandNames.HandYOffsetBasic,
                message => SetHandYOffsetBasic(message.ParseAsCentimeter())
                );
            receiver.AssignCommandHandler(
                MessageCommandNames.HandYOffsetAfterKeyDown,
                message => SetHandYOffsetAfterKeyDown(message.ParseAsCentimeter())
                );
            receiver.AssignCommandHandler(
                MessageCommandNames.EnablePresenterMotion,
                message => handIkIntegrator.EnablePresentationMode = message.ToBoolean()
                );
            receiver.AssignCommandHandler(
                MessageCommandNames.PresentationArmRadiusMin,
                message =>
                    handIkIntegrator.Presentation.PresentationArmRadiusMin = message.ParseAsCentimeter()
                );
            receiver.AssignCommandHandler(
                MessageCommandNames.LookAtStyle,
                message => headIkIntegrator.SetLookAtStyle(message.Content)
                );
            receiver.AssignCommandHandler(
                MessageCommandNames.EnableGamepad,
                message => _gamePad.SetEnableGamepad(message.ToBoolean())
                );
            receiver.AssignCommandHandler(
                MessageCommandNames.PreferDirectInputGamepad,
                message => _gamePad.SetPreferDirectInputGamepad(message.ToBoolean())
                );
            receiver.AssignCommandHandler(
                MessageCommandNames.GamepadLeanMode,
                message =>
                {
                    gamePadBasedBodyLean.SetGamepadLeanMode(message.Content);
                    smallGamepadHandIk.SetGamepadLeanMode(message.Content);
                });
            receiver.AssignCommandHandler(
                MessageCommandNames.GamepadLeanReverseHorizontal,
                message =>
                {
                    gamePadBasedBodyLean.ReverseGamepadStickLeanHorizontal = message.ToBoolean();
                    smallGamepadHandIk.ReverseGamepadStickLeanHorizontal = message.ToBoolean();
                });
            receiver.AssignCommandHandler(
                MessageCommandNames.GamepadLeanReverseVertical,
                message =>
                {
                    gamePadBasedBodyLean.ReverseGamepadStickLeanVertical = message.ToBoolean();
                    smallGamepadHandIk.ReverseGamepadStickLeanVertical = message.ToBoolean();
                });
            receiver.AssignCommandHandler(
                MessageCommandNames.SetDeviceTypeToStartWordToMotion,
                message => SetDeviceTypeForWordToMotion(message.ToInt())
                );
        }

        private void SetDeviceTypeForWordToMotion(int deviceType)
        {
            gamePadBasedBodyLean.UseGamepadForWordToMotion = (deviceType == DeviceTypeGamepad);
            handIkIntegrator.UseGamepadForWordToMotion = (deviceType == DeviceTypeGamepad);
            handIkIntegrator.UseKeyboardForWordToMotion = (deviceType == DeviceTypeKeyboardTenKey);
            handIkIntegrator.UseMidiControllerForWordToMotion = (deviceType == DeviceTypeMidiController);
        }

        //以下については適用先が1つじゃないことに注意

        private void SetLengthFromWristToTip(float v)
        {
            handIkIntegrator.Presentation.HandToTipLength = v;
            handIkIntegrator.Typing.HandToTipLength = v;
            handIkIntegrator.MouseMove.HandToTipLength = v;
            handIkIntegrator.MidiHand.WristToTipLength = v;
        }
        
        private void SetLengthFromWristToPalm(float v)
        {
            //handIkIntegrator.MouseMove.HandToPalmLength = v;
        }
        
        private void SetHandYOffsetBasic(float offset)
        {
            handIkIntegrator.Typing.YOffsetAlways = offset;
            handIkIntegrator.MouseMove.YOffset = offset;
            handIkIntegrator.MidiHand.HandOffsetAlways = offset;
        }

        private void SetHandYOffsetAfterKeyDown(float offset)
        {
            handIkIntegrator.Typing.YOffsetAfterKeyDown = offset;
            handIkIntegrator.MidiHand.HandOffsetAfterKeyDown = offset;
        }
    }
}
