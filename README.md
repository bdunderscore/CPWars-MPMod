# からぱりうぉーずネット対戦モッド

このプロジェクトは、「[からぱりうぉーず](https://store.steampowered.com/app/1988940/_/)」にネット対戦モードを追加する非公式モッドです。
現在製作中なので、いろいろとバグってるよ！詳しくは[バグトラッカー](https://github.com/bdunderscore/CPWars-MPMod/issues)を見てね。

# 注意事項

* **まずはセーブデータをバックアップしてください**

Win+Rを押して、コマンド欄に`%appdata%\..\locallow\KorimizuNoOmochabako`を入れてください。その中の`ColorfulPartyWars`フォルダーをコピーしてください。

* 実績がおかしくなる可能性があります（対策はしてるけど、一応バグがあったら解除してはいけない実績を解除する可能性はゼロではない）
* スチーム版・日本語のみに対応
* バグはたぶんかなり多いので報告してね

# 導入手順

1. [UnityModManager](https://www.nexusmods.com/site/mods/21?tab=files&file_id=1580) をダウンロード。登録が必要だけど、無料プランでOKです。もしくは[GitHubページの方に](https://github.com/newman55/unity-mod-manager/)登録なしの（ちょっと怪しげな）DLリンクもあります。
2. [UnityModManagerConfig.xml](https://github.com/bdunderscore/CPWars-MPMod/raw/main/UnityModManagerConfig.xml) を差し替えてください。
3. UnityModManager.exeを実行して、Installを押してください。

ここまでの手順は一回だけで大丈夫です。これ以降はモッドを更新するたびにやる必要があります。

1. リリースページから最新のモッドビルドをダウンロードしてください。
2. からぱりうぉーずのゲームフォルダーを開いてください（スチームで、右クリック→プロパティー→ローカルファイル→参照）
3. Modフォルダーの中の、`CPWars_Multiplayer`フォルダーがある場合は削除してください。
4. cpwars_multiplayer.zipをModsフォルダーに展開してください。

からぱりうぉーずを起動して、このような画面がでたら成功です。

![image](https://user-images.githubusercontent.com/64174065/177067015-3939e764-44e2-4dc3-a36b-e8246374fb36.png)

この画面は起動するたびにでますが、「Close」かCtrl+F10で消すことができます。自動的に開かなくする場合は、SettingsのShow this window on startupをNoに変えてください（その後はCtrl+F10で出せます）

# プレイする手順

1. メインメニューから「ネット対戦」を選択
2. 最初のPLが「部屋を作るを選択」
3. 部屋番号が表示されるので、他のPLに伝えましょう。![image](https://user-images.githubusercontent.com/64174065/177067399-b32b3d30-530f-4602-9fad-d0c9cd82179e.png)
4. 他のPLの方で、部屋番号を入力して「部屋に入る」を選択
5. 全員揃ったら、最初に入った人（ホスト）で「れでぃー」を押す　※[ほかの人が押しても反応しません](https://github.com/bdunderscore/CPWars-MPMod/issues/8)

「？？？部」のところをクリックすると、部活を選択できます。かぶりが出た場合は、ランダムで一人以外他の部活に割り振られます。

その右のところをクリックすると、初期メンバーを選択できます。ここもかぶりが出たらランダムになります。
