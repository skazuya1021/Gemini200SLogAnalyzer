# Gemini200S Log Analyzer

Gemini200S ウエハログ（.log）および ManualLog（.csv）の合体・分析・グラフ表示を行う Windows デスクトップアプリケーションです。

## 動作環境

- Windows 11
- .NET 8.0 Desktop Runtime

## ビルド・実行

```powershell
cd Gemini200SLogAnalyzer
dotnet run --project Gemini200SLogAnalyzer
```

## 主な機能

- ログファイルの読込み（複数ファイル / フォルダ一括）
- **ManualLog（.csv）** の読込み・合体（Wafer Log と切替可能）
- 日付順でのログ合体と CSV 出力
- 統計値分析（Median / Average / Max / Min）と Excel 保存
- データ表示の行仮想化（大量データでも高速表示・スクロール連動）
- グラフ表示（折れ線 / ドット / 面 / 棒 / 散布図）、**左右 Y 軸振り分け**（2 項目以上）
- 散布図の X 軸・Y 軸任意設定と複数組み合わせ描画
- グラフ表示範囲・選択項目の CSV 保存
- カーソル位置データ表示、2 点間差分測定（点1/点2を個別指定、時間指定自動マーキング対応）
- **変動確認** タブ（時間/値間隔での変化量分析・グラフ表示）
- ズーム連動 LOD（大量データの表示点数自動調整）
- PNG 形式でのグラフ保存（カーソル/差分データの注釈付き保存に対応）

## バージョン

現在のバージョン: **1.3.4**

詳細は [CHANGELOG.md](CHANGELOG.md) を参照してください。

## ライセンス

社内利用を想定したツールです。
