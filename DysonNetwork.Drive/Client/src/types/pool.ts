export interface SnFilePool {
  id: string
  name: string
  description: string
  storage_config: StorageConfig
  billing_config: BillingConfig
  policy_config: any
  public_indexable: boolean
  public_usable: boolean
  no_optimization: boolean
  no_metadata: boolean
  allow_encryption: boolean
  allow_anonymous: boolean
  require_privilege: number
  account_id: null
  resource_identifier: string
  created_at: Date
  updated_at: Date
  deleted_at: null
}

export interface BillingConfig {
  cost_multiplier: number
}

export interface StorageConfig {
  region: string
  bucket: string
  endpoint: string
  secret_id: string
  secret_key: string
  enable_signed: boolean
  enable_ssl: boolean
  image_proxy: null
  access_proxy: null
  expiration: null
}
