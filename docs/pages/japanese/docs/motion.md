---
layout: page
title: Motion
permalink: /docs/motion
---

[English](../en/docs/motion)

# モーション

`モーション`タブではキャラクターの動き方や体型にかんする調整ができます。

{% include docimg.html file="/images/docs/motion_top.png" %}

#### 顔・表情
{: .doc-sec2 }

`リップシンク`: リップシンク機能に用いるマイクを設定します。コントロールパネルのタブにもある機能です。

`顔をトラッキング`: ウェブカメラによる顔トラッキングに用いるカメラを設定します。コントロールパネルの配信タブにもある機能です。

`顔とセットで手もトラッキング`: ウェブカメラによる簡易的なハンドトラッキングを有効にします。コントロールパネルの配信タブにもある機能です。

`顔トラッキング中も自動でまばたき`: デフォルトではオンになっています。オフにすると、画像処理ベースで目の開閉を制御するようになります。

`顔トラッキング中の前後移動を有効化`: オンにすると、キャラクターが前後に動くようになります。顔トラッキングが安定している場合、このチェックをオンにするとキャラクターの動きが更にリッチになります。

`左右反転をオフにする`: チェックボックをオンにすると左右の反転がオフになります。このオプションを切り替えた場合は`姿勢・表情を補正`ボタンを押すようにして下さい。

`姿勢・表情を補正`: 押すことで現在のカメラに映っている姿勢でキャリブレーションします。コントロールパネルの配信タブにもある機能です。

`顔が動くときのまばたき補正`: 自動まばたきが有効なとき、このチェックがオンになっていると、首をすばやく動かしたときに高確率でまばたきします。

`会話の区切りでのまばたき補正`: 自動まばたきが有効で、かつリップシンクが有効なときにこのチェックがオンになっていると、発話の区切り目に高確率でまばたきします。

`視線の動き`: 視線をどう動かすか選択します。コントロールパネルの配信タブにもある機能です。

`Funブレンドのデフォルト値[%]`: ふだんの表情に`Fun`ブレンドシェイプを適用することで、やや笑顔の状態にするパラメータです。大きくするほど普段から笑顔になりますが、キャラクターによってはまばたきやリップシンクの動作と組み合わせたとき不自然になります。その場合は小さな値にします。

`眉毛(開いてカスタマイズ)`: 通常はカスタムする必要のない、高度な機能です。独自に作成したVRMで眉毛をうまく動かしたい場合や、眉毛の動きが大きすぎたり、小さすぎたりする場合にカスタマイズします。

※ここをカスタマイズするにはVRMの表情を操作する「ブレンドシェイプ」の知識が必要です。もし詳しくない場合、[バーチャルキャストWikiの説明](https://virtualcast.jp/wiki/doku.php?id=%E3%83%A2%E3%83%87%E3%83%AB%E4%BD%9C%E6%88%90:%E3%83%96%E3%83%AC%E3%83%B3%E3%83%89%E3%82%B7%E3%82%A7%E3%82%A4%E3%83%97%E8%A8%AD%E5%AE%9A)に記載の`ブレンドシェイプの値設定`などをご覧下さい。

1. `左眉上げ`: 左眉、あるいは両方の眉を上げるブレンドシェイプを指定します。
2. `左眉下げ`: 左眉、あるいは両方の眉を下げるブレンドシェイプを指定します。
3. `左右で別々のキーを使う`: 右と左の眉が別々に動かせるモデルの場合、チェックをオンにします。
4. `右眉上げ`: 右眉を上げるブレンドシェイプを指定します。
5. `右眉下げ`: 右眉を下げるブレンドシェイプを指定します。
6. `眉上げスケール [%]`と`眉下げスケール [%]`: 眉の上下の動きの大きさです。誇張したい場合は大きめの値を、大きくしすぎると不自然になる場合は小さめの値を指定します。

#### 腕・ひじ
{: .doc-sec2 }

腕やひじの動かし方を設定します。

`タイピング/マウス動作を反映`: チェックをオフにすると、タイピングやマウス、ゲームパッドの動作を行わなくなります。キャラクターを完全に棒立ちさせたい場合、このチェックをオフにします。

`脇をしめる幅 [cm]`: キャラが脇をひらく度合いを設定します。キャラクターの腰が太い場合は大きくし、細い場合は小さくします。

`脇をしめる強さ [%]`: ひじの開き方をどこまで強く適用するか決めるパラメータです。大きすぎる値を指定すると腕が体にめり込みやすくなります。

以下はデフォルトの設定、脇をきつくしめる設定、脇を開いた設定の例です。

<div class="row">
{% include docimg.html file="/images/docs/arm_side_default.png" customclass="col s12 m4 l4" imgclass="fit-doc-img" %}
{% include docimg.html file="/images/docs/arm_side_close.png" customclass="col s12 m4 l4" imgclass="fit-doc-img" %}
{% include docimg.html file="/images/docs/arm_side_open.png" customclass="col s12 m4 l4" imgclass="fit-doc-img" %}
</div>

`プレゼン風に右手を動かす`: チェックをオンにすると右手がマウスポインタの位置を指すようになります。

`補助ポインターを表示`: `プレゼン風に右手を動かす`のチェックがオンのとき、マウスポインターの強調として追加エフェクトを表示するかどうか設定します。

`プレゼン動作サイズ [%]`: このプロパティは現在使われていません。将来的に削除される予定です。

`プレゼン動作の最小半径[cm]`: プレゼン動作中に、右手が胴体にめり込むのを防ぐためのパラメータです。大きくすると手が体にめり込みにくくなる代わり、腕が伸びがちになります。


#### 手・指
{: .doc-sec2 }

手や指の長さと、打鍵動作の大きさを調整します。タイピング中の手の位置が大きくずれてしまう場合や、手がキーボードから浮きすぎる場合、調整してみてください。

`手首から指先までの長さ[cm]`: 手首から指先までの長さを設定します。キーボードやタッチパッドを触る際の位置合わせで使用します。

`手首から手のひらまでの長さ[cm]`: 現在使われていないパラメータです。将来的に削除されます。

`手の高さ調整[cm]`: タッチパッドなどを触るとき、手とデバイスのあいだに取る距離を設定します。

`(打鍵後)手の高さ調整[cm]`: タイピング後に手を持ち上げる高さを設定します。

**Hint:** 自然な動きに調整したのち、わざと`(打鍵後)手の高さ調整[cm]`の値だけを大きくすることで、大げさにタイピング動作するようにできます。

<div class="row">
{% include docimg.html file="/images/docs/large_typing_motion.png" customclass="col s12 m4 l4" imgclass="fit-doc-img" %}
</div>

#### 待機モーション
{: .doc-sec2 }

待機モーションは呼吸動作に相当する動きです。通常デフォルト設定のままで問題ありませんが、不自然な場合はオフにしたり、パラメータを調整します。

`待機モーションを有効化`: チェックをオンにすると待機モーションを行います。

`動きの大きさ[%]`: 動作の大きさを設定します。

`周期 [sec]`: 待機モーションの動きを1周あたり何秒かけて繰り返すかを設定します。