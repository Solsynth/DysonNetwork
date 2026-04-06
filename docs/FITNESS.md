# Fitness Service API

A fitness tracking service for managing workouts, exercises, goals, and metrics.

## Base URL

```
Production: https://solian.app/fitness
Local: http://localhost:5072
```

All endpoints require authentication (Bearer token).

**Note:** All JSON property names use `snake_case`.

---

## Visibility

All fitness records (workouts, metrics, goals) have a visibility field that controls who can see them.

| Value | Name | Description |
|-------|------|-------------|
| 0 | Private | Only the owner can see |
| 1 | Public | Anyone can embed in posts |

Default: `Private`

---

## Workouts

### List Workouts

```http
GET /api/workouts?skip=0&take=20
```

**Response:**
```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "account_id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "Morning Run",
    "description": "5km morning run",
    "type": 2,
    "start_time": "2026-04-05T06:00:00Z",
    "end_time": "2026-04-05T06:30:00Z",
    "duration": "PT30M",
    "calories_burned": 300,
    "visibility": 0,
    "notes": null,
    "created_at": "2026-04-05T05:55:00Z",
    "updated_at": "2026-04-05T05:55:00Z",
    "exercises": []
  }
]
```

**Headers:** `X-Total: 42`

---

### Create Workout

```http
POST /api/workouts
```

```json
{
  "name": "Upper Body",
  "type": 0,
  "start_time": "2026-04-05T18:00:00Z",
  "description": "Chest and back workout",
  "duration": "PT1H",
  "calories_burned": 450,
  "visibility": 1,
  "notes": "Felt strong today"
}
```

**Note:** 
- `WorkoutType` enum: `0=Strength, 1=Cardio, 2=HIIT, 3=Yoga, 4=Other`
- If `duration` is not provided, it will be auto-calculated from `end_time - start_time`
- Set `visibility: 1` to allow embedding in posts

---

### Get Workout

```http
GET /api/workouts/{id}
```

---

### Update Workout

```http
PUT /api/workouts/{id}
```

```json
{
  "name": "Upper Body (Updated)",
  "type": 0,
  "start_time": "2026-04-05T18:00:00Z",
  "description": "Chest, back, and shoulders",
  "duration": "PT1H15M",
  "calories_burned": 500,
  "visibility": 1
}
```

---

### Delete Workout

```http
DELETE /api/workouts/{id}
```

Returns `204 No Content` on success.

---

### Batch Create Workouts

```http
POST /api/workouts/batch
```

```json
{
  "workouts": [
    {
      "name": "Morning Run",
      "type": 1,
      "start_time": "2026-04-05T06:00:00Z",
      "end_time": "2026-04-05T06:30:00Z",
      "calories_burned": 300,
      "external_id": "strava-run-123",
      "visibility": 1
    },
    {
      "name": "Evening Yoga",
      "type": 3,
      "start_time": "2026-04-05T20:00:00Z",
      "duration": "PT45M",
      "visibility": 0
    }
  ]
}
```

**Features:**
- Auto-calculates duration from end_time - start_time if not provided
- Uses `external_id` for duplicate prevention (skips if already exists)
- Triggers goal recalculation for all workout types

---

### Add Exercise to Workout

```http
POST /api/workouts/{workoutId}/exercises
```

```json
{
  "exercise_name": "Bench Press",
  "sets": 4,
  "reps": 10,
  "weight": 60.5,
  "notes": "Good form",
  "order_index": 0
}
```

---

### Update Exercise

```http
PUT /api/workouts/exercises/{exerciseId}
```

```json
{
  "exercise_name": "Bench Press",
  "sets": 4,
  "reps": 12,
  "weight": 65.0,
  "order_index": 0
}
```

---

### Remove Exercise

```http
DELETE /api/workouts/exercises/{exerciseId}
```

Returns `204 No Content` on success.

---

## Goals

### List Goals

```http
GET /api/goals?status=0&skip=0&take=20
```

**Query Params:**
- `status`: Filter by `0=Active, 1=Completed, 2=Cancelled`

**Response:**
```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "account_id": "550e8400-e29b-41d4-a716-446655440000",
    "goal_type": 0,
    "title": "Lose 5kg",
    "description": "Summer body goal",
    "target_value": 75.0,
    "current_value": 78.5,
    "unit": "kg",
    "start_date": "2026-01-01T00:00:00Z",
    "end_date": "2026-06-01T00:00:00Z",
    "status": 0,
    "visibility": 0,
    "bound_workout_type": null,
    "bound_metric_type": null,
    "auto_update_progress": true,
    "repeat_type": 0,
    "repeat_interval": 1,
    "repeat_count": null,
    "notes": null,
    "created_at": "2026-01-01T00:00:00Z",
    "updated_at": "2026-04-05T00:00:00Z"
  }
]
```

**Note:** `FitnessGoalType`: `0=WeightLoss, 1=MuscleGain, 2=Endurance, 3=Steps, 4=Custom`

---

### Get Goal Statistics

```http
GET /api/goals/stats
```

**Response:**
```json
{
  "active_count": 3,
  "completed_count": 5
}
```

---

### Create Goal

```http
POST /api/goals
```

```json
{
  "title": "Run 100km this month",
  "goal_type": 2,
  "start_date": "2026-04-01T00:00:00Z",
  "target_value": 100.0,
  "current_value": 45.0,
  "unit": "km",
  "end_date": "2026-04-30T00:00:00Z",
  "description": "Monthly running goal",
  "visibility": 1,
  "bound_workout_type": 1,
  "auto_update_progress": true,
  "repeat_type": 1,
  "repeat_interval": 1,
  "repeat_count": 12
}
```

**Goal Binding:**
- `bound_workout_type`: Auto-update progress when workouts of this type are logged
- `bound_metric_type`: Auto-update progress when metrics of this type are recorded
- `auto_update_progress`: Enable/disable automatic progress updates

**Repeatable Goals:**
- `repeat_type`: `0=None, 1=Daily, 2=Weekly, 3=Monthly`
- `repeat_interval`: Interval between repetitions (e.g., 2 = every 2 weeks)
- `repeat_count`: Total repetitions (null = unlimited)

---

### Update Goal

```http
PUT /api/goals/{id}
```

```json
{
  "title": "Run 100km this month",
  "goal_type": 2,
  "start_date": "2026-04-01T00:00:00Z",
  "status": 0,
  "target_value": 100.0,
  "current_value": 50.0,
  "unit": "km",
  "end_date": "2026-04-30T00:00:00Z",
  "visibility": 1,
  "bound_workout_type": 1,
  "auto_update_progress": true
}
```

---

### Update Progress

```http
PATCH /api/goals/{id}/progress
```

```json
{
  "current_value": 55.0
}
```

**Note:** Disabled if `auto_update_progress` is true and goal has bound workout/metric type.

---

### Update Status

```http
PATCH /api/goals/{id}/status
```

```json
{
  "status": 1
}
```

When status changes to `Completed`, a new goal is automatically created for repeatable goals.

---

### Recalculate Goal

```http
PATCH /api/goals/{id}/recalculate
```

Manually recalculates goal progress from bound workout/metric data.

**Response:** Updated goal with recalculated `current_value`

---

### Get Goal History

```http
GET /api/goals/{id}/history
```

Returns all related goals including previous repetitions for repeatable goals.

---

### Delete Goal

```http
DELETE /api/goals/{id}
```

---

## Metrics

### List Metrics

```http
GET /api/metrics?type=0&skip=0&take=20
```

**Query Params:**
- `type`: Filter by metric type (optional)

**Response:**
```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "account_id": "550e8400-e29b-41d4-a716-446655440000",
    "metric_type": 0,
    "value": 80.5,
    "unit": "kg",
    "recorded_at": "2026-04-05T08:00:00Z",
    "visibility": 0,
    "notes": "Morning weight",
    "source": "manual",
    "created_at": "2026-04-05T08:00:00Z",
    "updated_at": "2026-04-05T08:00:00Z"
  }
]
```

**Note:** `FitnessMetricType`: `0=Weight, 1=BodyFat, 2=Steps, 3=Distance, 4=HeartRate, 5=Sleep, 6=Custom`

---

### Get Latest Metrics

```http
GET /api/metrics/latest
```

Returns the most recent metric for each type.

**Response:**
```json
{
  "0": {
    "id": "...",
    "metric_type": 0,
    "value": 80.5,
    "unit": "kg",
    "recorded_at": "2026-04-05T08:00:00Z"
  },
  "2": {
    "id": "...",
    "metric_type": 2,
    "value": 8500,
    "unit": "steps",
    "recorded_at": "2026-04-05T22:00:00Z"
  }
}
```

---

### Create Metric

```http
POST /api/metrics
```

```json
{
  "metric_type": 0,
  "value": 79.8,
  "unit": "kg",
  "recorded_at": "2026-04-05T07:00:00Z",
  "visibility": 1,
  "notes": "After breakfast",
  "source": "manual"
}
```

---

### Update Metric

```http
PUT /api/metrics/{id}
```

```json
{
  "metric_type": 0,
  "value": 79.5,
  "unit": "kg",
  "recorded_at": "2026-04-05T07:00:00Z",
  "visibility": 1,
  "notes": "After dinner"
}
```

---

### Batch Create Metrics

```http
POST /api/metrics/batch
```

```json
{
  "metrics": [
    {
      "metric_type": 0,
      "value": 80.0,
      "unit": "kg",
      "recorded_at": "2026-04-05T07:00:00Z",
      "external_id": "scale-123",
      "visibility": 1
    },
    {
      "metric_type": 2,
      "value": 10000,
      "unit": "steps",
      "recorded_at": "2026-04-05T22:00:00Z",
      "visibility": 0
    }
  ]
}
```

**Features:**
- Uses `external_id` for duplicate prevention
- Triggers goal recalculation for all metric types

---

### Delete Metric

```http
DELETE /api/metrics/{id}
```

---

## Account Management

### Delete Account Data

```http
DELETE /api/fitness/account
```

Permanently deletes all fitness data (workouts, metrics, goals, exercise library) for the current user.

**Response:** `204 No Content`

---

## Embedding Fitness in Posts

Users can embed their fitness records in posts on Sphere. The fitness record must:
1. Be owned by the user creating the post
2. Have `visibility: 1` (Public)

### Format

Use `FitnessReference` field in PostRequest:

```
workout:{workout_id}
metric:{metric_id}
goal:{goal_id}
```

### Example

```json
{
  "title": "My morning run",
  "content": "Great run today!",
  "fitness_reference": "workout:550e8400-e29b-41d4-a716-446655440000"
}
```

The client retrieves fitness data via the Fitness API:
- `/api/workouts/{id}`
- `/api/metrics/{id}`
- `/api/goals/{id}`

---

## Exercise Library

### List Exercises

```http
GET /api/exercises?category=0&skip=0&take=20
```

**Query Params:**
- `category`: Filter by exercise category (optional)

**Response:**
```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "Bench Press",
    "description": "Classic chest exercise",
    "category": 0,
    "muscle_groups": ["chest", "triceps", "shoulders"],
    "difficulty": 1,
    "equipment": ["barbell", "bench"],
    "is_public": true,
    "account_id": null,
    "created_at": "2026-01-01T00:00:00Z",
    "updated_at": "2026-01-01T00:00:00Z"
  }
]
```

**Note:** 
- `ExerciseCategory`: `0=Chest, 1=Back, 2=Legs, 3=Arms, 4=Shoulders, 5=Core, 6=Cardio, 7=Other`
- `ExerciseDifficulty`: `0=Beginner, 1=Intermediate, 2=Advanced`

---

### Create Exercise

```http
POST /api/exercises
```

```json
{
  "name": "Incline Dumbbell Press",
  "description": "Upper chest focused",
  "category": 0,
  "muscle_groups": ["chest", "shoulders"],
  "difficulty": 1,
  "equipment": ["dumbbell", "bench"],
  "is_public": true
}
```

---

## Error Responses

All endpoints may return:

- `401 Unauthorized`: Invalid or missing token
- `403 Forbidden`: Accessing another user's data
- `404 Not Found`: Resource not found
- `400 Bad Request`: Invalid request body

---

## Type Reference

| Enum | Values |
|------|--------|
| WorkoutType | 0=Strength, 1=Cardio, 2=HIIT, 3=Yoga, 4=Other |
| FitnessGoalType | 0=WeightLoss, 1=MuscleGain, 2=Endurance, 3=Steps, 4=Custom |
| FitnessGoalStatus | 0=Active, 1=Completed, 2=Paused, 3=Cancelled |
| FitnessMetricType | 0=Weight, 1=BodyFat, 2=Steps, 3=Distance, 4=HeartRate, 5=Sleep, 6=Custom |
| ExerciseCategory | 0=Chest, 1=Back, 2=Legs, 3=Arms, 4=Shoulders, 5=Core, 6=Cardio, 7=Other |
| ExerciseDifficulty | 0=Beginner, 1=Intermediate, 2=Advanced |
| FitnessVisibility | 0=Private, 1=Public |
| RepeatType | 0=None, 1=Daily, 2=Weekly, 3=Monthly |
