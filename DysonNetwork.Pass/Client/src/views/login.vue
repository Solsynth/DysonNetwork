<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { NCard, NSpace, NInput, NButton, NSpin, NAlert, NProgress } from 'naive-ui'
import { useRouter } from 'vue-router'
import FingerprintJS from '@fingerprintjs/fingerprintjs'

// State management
const stage = ref<'find-account' | 'select-factor' | 'enter-code' | 'token-exchange'>(
  'find-account',
)
const isLoading = ref(false)
const error = ref<string | null>(null)

// Stage 1: Find Account
const accountIdentifier = ref('')
const deviceId = ref('')

// Stage 2 & 3: Challenge
const challenge = ref<any>(null)
const factors = ref<any[]>([])
const selectedFactorId = ref<string | null>(null)
const password = ref('') // Used for password or verification code

const router = useRouter()

// Generate deviceId based on browser fingerprint
onMounted(async () => {
  const fp = await FingerprintJS.load()
  const result = await fp.get()
  deviceId.value = result.visitorId
  localStorage.setItem('deviceId', deviceId.value)
})

const selectedFactor = computed(() => {
  if (!selectedFactorId.value) return null
  return factors.value.find((f) => f.id === selectedFactorId.value)
})

async function handleFindAccount() {
  if (!accountIdentifier.value) {
    error.value = 'Please enter your email or username.'
    return
  }
  isLoading.value = true
  error.value = null

  try {
    const response = await fetch('/api/auth/challenge', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        platform: 1,
        account: accountIdentifier.value,
        device_id: deviceId.value,
      }),
    })

    if (!response.ok) {
      const message = await response.text()
      throw new Error(message || 'Account not found.')
    }

    challenge.value = await response.json()
    await getFactors()
    stage.value = 'select-factor'
  } catch (e: any) {
    error.value = e.message
  } finally {
    isLoading.value = false
  }
}

async function getFactors() {
  isLoading.value = true
  error.value = null
  try {
    const response = await fetch(`/api/auth/challenge/${challenge.value.id}/factors`)
    if (!response.ok) {
      throw new Error('Could not fetch authentication factors.')
    }
    const availableFactors = await response.json()
    factors.value = availableFactors.filter(
      (f: any) => !challenge.value.blacklist_factors.includes(f.id),
    )
    if (factors.value.length > 0) {
      selectedFactorId.value = null // Let user choose
    } else if (challenge.value.step_remain > 0) {
      error.value =
        'No more available authentication factors, but authentication is not complete. Please contact support.'
    }
  } catch (e: any) {
    error.value = e.message
  } finally {
    isLoading.value = false
  }
}

async function requestVerificationCode(hint: string | null) {
  if (!selectedFactorId.value) return

  const isResend = stage.value === 'enter-code'
  if (isResend) isLoading.value = true
  error.value = null

  try {
    const response = await fetch(
      `/api/auth/challenge/${challenge.value.id}/factors/${selectedFactorId.value}`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(hint),
      },
    )
    if (!response.ok) {
      const message = await response.text()
      throw new Error(message || 'Failed to send code.')
    }
  } catch (e: any) {
    error.value = e.message
    throw e // Rethrow to be handled by caller
  } finally {
    if (isResend) isLoading.value = false
  }
}

async function handleFactorSelected() {
  if (!selectedFactor.value) {
    error.value = 'Please select an authentication method.'
    return
  }

  // For password or TOTP, just move to the next step
  if (selectedFactor.value.type === 0 || selectedFactor.value.type === 2) {
    stage.value = 'enter-code'
    return
  }

  // For email, send the code first
  if (selectedFactor.value.type === 1) {
    isLoading.value = true
    error.value = null
    try {
      await requestVerificationCode(selectedFactor.value.contact)
      stage.value = 'enter-code'
    } catch {
      // Error is already set by requestVerificationCode
    } finally {
      isLoading.value = false
    }
  }
}

async function handleVerifyFactor() {
  if (!selectedFactorId.value || !password.value) {
    error.value = 'Please enter your password/code.'
    return
  }
  isLoading.value = true
  error.value = null

  try {
    const response = await fetch(`/api/auth/challenge/${challenge.value.id}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        factor_id: selectedFactorId.value,
        password: password.value,
      }),
    })

    if (!response.ok) {
      const message = await response.text()
      throw new Error(message || 'Verification failed.')
    }

    challenge.value = await response.json()
    password.value = ''

    if (challenge.value.step_remain === 0) {
      stage.value = 'token-exchange'
      await exchangeToken()
    } else {
      await getFactors()
      stage.value = 'select-factor' // MFA step
    }
  } catch (e: any) {
    error.value = e.message
  } finally {
    isLoading.value = false
  }
}

async function exchangeToken() {
  isLoading.value = true
  error.value = null
  try {
    const response = await fetch('/api/auth/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        grant_type: 'authorization_code',
        code: challenge.value.id,
      }),
    })

    if (!response.ok) {
      const message = await response.text()
      throw new Error(message || 'Token exchange failed.')
    }

    const { token } = await response.json()
    localStorage.setItem('authToken', token)
    await router.push('/')
  } catch (e: any) {
    error.value = e.message
    stage.value = 'select-factor' // Go back if token exchange fails
  } finally {
    isLoading.value = false
  }
}

function getFactorName(factorType: number) {
  switch (factorType) {
    case 0:
      return 'Password'
    case 1:
      return 'Email'
    case 2:
      return 'Authenticator App'
    default:
      return 'Unknown Factor'
  }
}
</script>

<template>
  <div class="flex items-center justify-center h-full">
    <n-card class="w-full max-w-md" title="Login">
      <n-spin :show="isLoading">
        <n-space vertical>
          <!-- Stage 1: Find Account -->
          <div v-if="stage === 'find-account'">
            <p>Welcome back!</p>
            <p class="mb-4">Login with your Solarpass.</p>
            <p class="mb-4">Enter your account identifier to continue.</p>
            <n-input
              v-model:value="accountIdentifier"
              placeholder="Email or Username"
              size="large"
              @keydown.enter="handleFindAccount"
              class="mb-4"
            />
            <n-button type="primary" block class="mt-4" size="large" @click="handleFindAccount">
              Continue
            </n-button>
          </div>

          <!-- Stage 2: Select Factor -->
          <div v-if="stage === 'select-factor' && challenge">
            <div class="flex items-center mb-4 gap-3">
              <span class="flex-shrink-1">Completeness</span>
              <n-progress
                type="line"
                :percentage="(1 - challenge.step_remain / challenge.step_total) * 100"
                indicator-placement="inside"
                class="flex-1"
              />
            </div>

            <div class="flex flex-col gap-3">
              <n-card
                v-for="factor in factors"
                :key="factor.id"
                size="small"
                hoverable
                class="cursor-pointer"
                @click="
                  () => {
                    selectedFactorId = factor.id
                    handleFactorSelected()
                  }
                "
                :title="getFactorName(factor.type)"
              ></n-card>
            </div>
            <p class="text-center text-xs opacity-75 mt-3">Select a method to authenticate</p>
          </div>

          <!-- Stage 3: Enter Code -->
          <div v-if="stage === 'enter-code' && selectedFactor">
            <h3 class="mb-3">
              Enter the {{ selectedFactor.type === 0 ? 'password' : 'verification code' }} to
              continue.
            </h3>
            <p v-if="selectedFactor.type === 1">
              A code has been sent to {{ selectedFactor.contact }}.
            </p>
            <p v-if="selectedFactor.type === 2">Enter the code from your authenticator app.</p>
            <n-input
              v-model:value="password"
              type="password"
              show-password-on="click"
              :placeholder="selectedFactor.type === 0 ? 'Password' : 'Code'"
              size="large"
              class="mb-4"
              @keydown.enter="handleVerifyFactor"
            />
            <n-space justify="end">
              <n-button
                v-if="selectedFactor.type === 1"
                text
                @click="requestVerificationCode(selectedFactor.contact)"
              >
                Resend Code
              </n-button>
            </n-space>
            <n-button type="primary" block class="mt-4" size="large" @click="handleVerifyFactor">
              Verify
            </n-button>
          </div>

          <!-- Stage 4: Token Exchange -->
          <div v-if="stage === 'token-exchange'">
            <h3 class="mb-4">Finalizing Login</h3>
            <n-spin />
          </div>

          <n-alert
            v-if="error"
            title="Error"
            type="error"
            closable
            @after-hide="error = null"
            class="mt-2"
          >
            {{ error }}
          </n-alert>
        </n-space>
      </n-spin>
    </n-card>
  </div>
</template>
