#!/bin/bash
set -e

BASE="/Users/littlesheep/Documents/Projects/SolarNetwork/DysonNetwork"
SHARED_MODELS="$BASE/DysonNetwork.Shared/Models"

move_models() {
    local service=$1
    shift
    local models=("$@")
    local service_dir="$BASE/$service"
    local target_dir="$service_dir/Models"
    
    echo "=== Moving ${#models[@]} models to $service ==="
    
    mkdir -p "$target_dir"
    
    for model in "${models[@]}"; do
        # Find source file (might be in subdirectories)
        src=$(find "$SHARED_MODELS" -name "${model}.cs" -type f | head -1)
        if [ -z "$src" ]; then
            echo "  WARN: $model.cs not found in Shared"
            continue
        fi
        
        # Determine relative path to preserve subdirectory structure
        rel_path="${src#$SHARED_MODELS/}"
        dest_dir="$target_dir/$(dirname "$rel_path")"
        mkdir -p "$dest_dir"
        
        echo "  $model: $(basename "$src") -> $service/Models/$rel_path"
        
        # Copy file
        cp "$src" "$dest_dir/$(basename "$src")"
        
        # Update namespace: DysonNetwork.Shared.Models -> DysonNetwork.{Service}.Models
        # Handle subdirectories too
        old_ns="DysonNetwork.Shared.Models"
        new_ns="DysonNetwork.${service#DysonNetwork.}.Models"
        
        # If in a subdirectory, append that to namespace
        sub_dir=$(dirname "$rel_path")
        if [ "$sub_dir" != "." ]; then
            old_ns="$old_ns.${sub_dir//\//.}"
            new_ns="$new_ns.${sub_dir//\//.}"
        fi
        
        sed -i '' "s/namespace $old_ns/namespace $new_ns/" "$dest_dir/$(basename "$src")"
        
        # Remove source
        rm "$src"
    done
    
    # Remove empty directories in Shared/Models
    find "$SHARED_MODELS" -type d -empty -delete 2>/dev/null || true
}

# Padlock
PADLOCK_MODELS=("AuthorizedApp" "Punishment")
move_models "DysonNetwork.Padlock" "${PADLOCK_MODELS[@]}"

# Passport
PASSPORT_MODELS=("AccountEvent" "Nearby" "RewindPoint")
move_models "DysonNetwork.Passport" "${PASSPORT_MODELS[@]}"

# Sphere
SPHERE_MODELS=("ActivityHeatmap" "ActivityPubDelivery" "AutomodRule" "DiscoveryProfile" "FediverseActor" "FediverseInstance" "FediverseKey" "FediverseModerationRule" "FediverseRelationship" "ICloudFile" "LiveStream" "LiveStreamAward" "LiveStreamAwardAttitude" "LiveStreamChatMessage" "PostIndex" "PostInterestProfile")
move_models "DysonNetwork.Sphere" "${SPHERE_MODELS[@]}"

# Messager
MESSAGER_MODELS=("ChatMessagePin" "ChatRoom" "RealtimeCall")
move_models "DysonNetwork.Messager" "${MESSAGER_MODELS[@]}"

# Develop
DEVELOP_MODELS=("DevProject" "MiniApp")
move_models "DysonNetwork.Develop" "${DEVELOP_MODELS[@]}"

# Wallet
WALLET_MODELS=("SubscriptionCatalog")
move_models "DysonNetwork.Wallet" "${WALLET_MODELS[@]}"

# Insight
INSIGHT_MODELS=("LinkEmbed" "ThinkingSequence" "UnpaidAccount" "WebArticle")
move_models "DysonNetwork.Insight" "${INSIGHT_MODELS[@]}"

echo "=== Done moving all models ==="
