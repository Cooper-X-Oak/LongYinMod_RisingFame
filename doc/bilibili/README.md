# Bilibili 资料目录

更新时间：`2026-03-25`

## 目录说明

- `raw/search/`
  - B 站搜索页原始结果
  - 包含 `龙胤mod` 以及扩展关键词的分页 JSON 和汇总 Markdown

- `raw/subtitles/`
  - 通过本地 `bilibili-subtitle` 流程抓下来的字幕 Markdown
  - 文件带 YAML 元数据和纯文本字幕正文

- `raw/comments/`
  - 多个核心视频的热门评论原始 JSON
  - 以及清洗后的 `*-hot-comments.md`

- `raw/meta/`
  - 视频基础元数据（view 接口）

- `raw/comments-summary.md`
  - 评论关键词命中摘要

## 推荐阅读顺序

1. `../bilibili-demand-analysis.md`
2. `raw/comments-summary.md`
3. `raw/subtitles/` 里的核心视频字幕
4. `raw/comments/` 里的具体评论样本
