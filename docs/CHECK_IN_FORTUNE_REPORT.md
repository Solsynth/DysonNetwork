# Check-In Fortune Report

Passport daily check-in can store a versioned, personalized fortune report on `SnCheckInResult.FortuneReport` for clients that opt into check-in API version `2` or newer.

The programmed check-in roll remains the source of truth. MiChan only rewrites the user-facing `tips` and enriches the existing result into `fortune_report` copy.

## Storage

`SnCheckInResult` includes:

```csharp
[Column(TypeName = "jsonb")]
public CheckInFortuneReport? FortuneReport { get; set; }
```

The JSON response uses snake_case:

```json
{
  "level": "Better",
  "tips": [
    {
      "is_positive": true,
      "title": "铃声渐明",
      "content": "适合把一件拖延的小事收尾，节奏会因此更轻。"
    },
    {
      "is_positive": false,
      "title": "急语宜缓",
      "content": "做决定前先停一停，避免被一时情绪带偏。"
    }
  ],
  "fortune_report": {
    "version": 2,
    "poem": "风过签筒静，铃响见微光。",
    "summary": "今日整体气息偏明朗，适合把拖延的小事重新拾起。",
    "summary_detail": "今日建议你先把节奏放稳，再选择最值得推进的一件事。若遇到卡顿，不必急着证明自己，先整理线索、减少分心；把注意力放在能立即收尾的小行动上，会比反复犹豫更有帮助。",
    "wish": "愿望宜从一个小动作开始。",
    "love": "温柔表达比反复猜测更有力量。",
    "study": "适合整理旧知识，细节里会有新线索。",
    "career": "先稳住手边事务，再推进新的判断。",
    "health": "留意休息和饮水，不必把自己逼得太紧。",
    "lost_item": "先看常用包袋和桌角附近。",
    "lucky_color": "浅青色",
    "lucky_direction": "东南",
    "lucky_time": "午后",
    "lucky_item": "随身钥匙",
    "lucky_action": "把一件拖延的小事收尾。",
    "avoid_action": "避免在情绪上头时立刻做决定。",
    "ritual": "出门前整理桌面一角，给今天留出清爽的开端。"
  }
}
```

## Versioning

The check-in endpoints accept a `version` query parameter. It defaults to `1`.

| Endpoint | `version < 2` | `version >= 2` |
| --- | --- | --- |
| `POST /api/accounts/me/check-in` | performs legacy check-in with programmed localized `tips` and stores no `fortune_report` | asks MiChan to generate personalized `tips` and `fortune_report`, then stores both |
| `GET /api/accounts/me/check-in` | hides `fortune_report` even if the row has one | returns `fortune_report` when present |

`fortune_report.version` is the programmed fortune schema/prompt version, separate from the endpoint compatibility version.

Current fortune report version: `2`.

Use a new version when the stored schema or semantic contract changes. Old check-ins keep their original version so clients and future migrations can understand historical rows.

## Programmed Draw

The check-in service still rolls `CheckInResultLevel` in code. MiChan is told the draw label and must not change it.

Level mapping:

| `CheckInResultLevel` | Draw Label |
| --- | --- |
| `Best` | `上上签` |
| `Better` | `上签` |
| `Normal` | `中签` |
| `Worse` | `下签` |
| `Worst` | `下下签` |
| `Special` | `特别签` |

Birthday check-ins use `Special` and skip the normal random roll, preserving existing behavior.

## MiChan Generation

`AccountEventService.CheckInDaily` calls Insight gRPC through `DyAgentCompletionService`.

Request settings:

| Field | Value |
| --- | --- |
| `persona` | `DY_AGENT_PERSONA_MICHAN` |
| `account_id` | current account ID |
| `topic` | `每日签到运势 v2` |
| `enable_tools` | `false` |
| `thinking` | `false` |
| `reasoning_effort` | `low` |
| deadline | 10 seconds |

Tools and persistence are disabled because check-in fortune generation should be a bounded copywriting task, not an autonomous action or memory update.

## Prompt Inputs

MiChan receives:

- account nickname, username, language, and region
- user-local check-in date
- birthday flag
- backdated flag
- programmed draw label, such as `上上签` or `下签`
- raw `CheckInResultLevel`
- legacy programmed tips as source material
- public same-day personal calendar events
- same-day global or regional notable days

Private user calendar events are not sent to MiChan. Friend-only events are also not sent. Only `EventVisibility.Public` events are included.

Notable days come from `NotableDaysService`, which combines regional holidays from Nager.Holiday with global days such as International Workers' Day, Christmas, New Year's Day, and Solar Network anniversary.

## Output Contract

MiChan must return a directly parseable JSON object with all fields present:

```json
{
  "tips": [
    { "is_positive": true, "title": "吉提示标题", "content": "具体提示" },
    { "is_positive": true, "title": "吉提示标题", "content": "具体提示" },
    { "is_positive": false, "title": "忌提示标题", "content": "具体提醒" },
    { "is_positive": false, "title": "忌提示标题", "content": "具体提醒" }
  ],
  "fortune_report": {
    "version": 2,
    "poem": "签诗，1到2句，有意象但自然",
    "summary": "运势总评，60字以内",
    "summary_detail": "今日建议，120到180字，像巫女基于签位给用户的具体行动建议",
    "wish": "愿望，40字以内",
    "love": "爱情，40字以内",
    "study": "学业，40字以内",
    "career": "事业，40字以内",
    "health": "健康，40字以内",
    "lost_item": "失物，40字以内",
    "lucky_color": "幸运色，短词",
    "lucky_direction": "幸运方位，短词",
    "lucky_time": "幸运时段，短词",
    "lucky_item": "幸运小物，短词",
    "lucky_action": "今日宜做，40字以内",
    "avoid_action": "今日忌做，40字以内",
    "ritual": "小仪式，60字以内"
  }
}
```

The parser tolerates accidental fenced code blocks but requires a valid object, exactly four tips, and all required `fortune_report` fields.

MiChan must generate all user-facing strings in the account's preferred language when available. If the preferred language cannot be determined, it falls back to Simplified Chinese.

## Failure Behavior

Check-in must not fail because Insight is unavailable.

If gRPC fails, times out, or returns invalid JSON, Passport logs a warning and stores the programmed tips plus a local fallback `CheckInFortuneReport` from the programmed level and tips.

Rewards, check-in availability, captcha behavior, and `version < 2` legacy `tips` remain unchanged.

## Migration

The feature adds nullable `fortune_report jsonb` to `account_check_in_results`.

Migration:

```text
DysonNetwork.Passport/Migrations/20260504191805_AddCheckInFortuneReport.cs
```
