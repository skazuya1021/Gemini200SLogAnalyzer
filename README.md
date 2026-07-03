# Gemini200S Log Analyzer

Gemini200S ウエハログ（.log）の合体・分析・グラフ表示を行う Windows デスクトップアプリケーションです。

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
- 日付順でのログ合体と CSV 出力
- 統計値分析（Median / Average / Max / Min）と Excel 保存
- グラフ表示（折れ線 / ドット / 面 / 棒 / 散布図）
- 散布図の X 軸・Y 軸任意設定と複数組み合わせ描画
- PNG 形式でのグラフ保存

## バージョン

現在のバージョン: **1.1.0**

詳細は [CHANGELOG.md](CHANGELOG.md) を参照してください。

## ライセンス

社内利用を想定したツールです。
