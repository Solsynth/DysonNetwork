#!/bin/bash
set -e

BASE="/Users/littlesheep/Documents/Projects/SolarNetwork/DysonNetwork"
SHARED_MODELS="$BASE/DysonNetwork.Shared/Models"

echo "=== Checking model structure ==="
for model in AuthorizedApp Punishment; do
    find "$SHARED_MODELS" -name "${model}.cs" -type f
done
