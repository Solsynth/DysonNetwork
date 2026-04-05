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
  "notes": "Felt strong today"
}
```

**Note:** `WorkoutType` enum: `0=Strength, 1=Cardio, 2=HIIT, 3=Yoga, 4=Other`

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
  "calories_burned": 500
}
```

---

### Delete Workout

```http
DELETE /api/workouts/{id}
```

Returns `204 No Content` on success.

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
  "description": "Monthly running goal"
}
```

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
  "end_date": "2026-04-30T00:00:00Z"
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
    "notes": "Morning weight",
    "source": "manual",
    "created_at": "2026-04-05T08:00:00Z",
    "updated_at": "2026-04-05T08:00:00Z"
  }
]
```

**Note:** `FitnessMetricType`: `0=Weight, 1=BodyFat, 2=Steps, 3=Distance, 4=HeartRate, 5=Sleep, 6=Custom`

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
  "notes": "After breakfast"
}
```

---

### Delete Metric

```http
DELETE /api/metrics/{id}
```

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
| FitnessGoalStatus | 0=Active, 1=Completed, 2=Cancelled |
| ExerciseCategory | 0=Chest, 1=Back, 2=Legs, 3=Arms, 4=Shoulders, 5=Core, 6=Cardio, 7=Other |
| ExerciseDifficulty | 0=Beginner, 1=Intermediate, 2=Advanced |
| FitnessMetricType | 0=Weight, 1=BodyFat, 2=Steps, 3=Distance, 4=HeartRate, 5=Sleep, 6=Custom |