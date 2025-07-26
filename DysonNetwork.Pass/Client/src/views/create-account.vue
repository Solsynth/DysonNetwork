<template>
  <div class="flex items-center justify-center h-full">
    <n-card class="w-full max-w-md" title="Create a new Solar Network ID">
      <n-spin :show="isLoading">
        <n-form
          ref="formRef"
          :model="formModel"
          :rules="rules"
          @submit.prevent="handleCreateAccount"
        >
          <n-form-item path="name" label="Username">
            <n-input v-model:value="formModel.name" size="large" />
          </n-form-item>
          <n-form-item path="nick" label="Nickname">
            <n-input v-model:value="formModel.nick" size="large" />
          </n-form-item>
          <n-form-item path="email" label="Email">
            <n-input v-model:value="formModel.email" placeholder="your@email.com" size="large" />
          </n-form-item>
          <n-form-item path="password" label="Password">
            <n-input
              v-model:value="formModel.password"
              type="password"
              show-password-on="click"
              placeholder="Enter your password"
              size="large"
            />
          </n-form-item>

          <n-form-item path="captchaToken">
            <div class="flex justify-center w-full">
              <captcha-widget
                :provider="captchaProvider"
                :api-key="captchaApiKey"
                @verified="onCaptchaVerified"
              />
            </div>
          </n-form-item>

          <n-button type="primary" attr-type="submit" block size="large" :disabled="isLoading">
            Create Account
          </n-button>

          <div class="mt-3 text-sm text-center opacity-75">
            <n-button text block @click="router.push('/login')" size="tiny">
              Already have an account? Login
            </n-button>
          </div>
        </n-form>
        <n-alert
          v-if="error"
          title="Error"
          type="error"
          closable
          @close="error = null"
          class="mt-4"
        >
          {{ error }}
        </n-alert>
      </n-spin>
    </n-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useRouter } from 'vue-router'
import {
  NCard,
  NInput,
  NButton,
  NSpin,
  NAlert,
  NForm,
  NFormItem,
  type FormInst,
  type FormRules,
  useMessage,
} from 'naive-ui'
import CaptchaWidget from '@/components/CaptchaWidget.vue'

const router = useRouter()
const formRef = ref<FormInst | null>(null)

const isLoading = ref(false)
const error = ref<string | null>(null)

const formModel = reactive({
  name: '',
  nick: '',
  email: '',
  password: '',
  language: 'en-us',
  captchaToken: '',
})

const rules: FormRules = {
  name: [
    { required: true, message: 'Please enter a username', trigger: 'blur' },
    {
      pattern: /^[A-Za-z0-9_-]+$/,
      message: 'Username can only contain letters, numbers, underscores, and hyphens.',
      trigger: 'blur',
    },
  ],
  nick: [{ required: true, message: 'Please enter a nickname', trigger: 'blur' }],
  email: [
    { required: true, message: 'Please enter your email', trigger: 'blur' },
    { type: 'email', message: 'Please enter a valid email address', trigger: ['input', 'blur'] },
  ],
  password: [
    { required: true, message: 'Please enter a password', trigger: 'blur' },
    { min: 4, message: 'Password must be at least 4 characters long', trigger: 'blur' },
  ],
  captchaToken: [{ required: true, message: 'Please complete the captcha verification.' }],
}

// Get captcha provider and API key from global data
const captchaProvider = ref((window as any).__APP_DATA__?.Provider || '')
const captchaApiKey = ref((window as any).__APP_DATA__?.ApiKey || '')

const onCaptchaVerified = (token: string) => {
  formModel.captchaToken = token
}

const messageDisplay = useMessage()

function handleCreateAccount(e: Event) {
  e.preventDefault()
  formRef.value?.validate(async (errors) => {
    if (errors) {
      return
    }

    isLoading.value = true
    error.value = null

    try {
      const response = await fetch('/api/accounts', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: formModel.name,
          nick: formModel.nick,
          email: formModel.email,
          password: formModel.password,
          language: formModel.language,
          captcha_token: formModel.captchaToken,
        }),
      })

      if (!response.ok) {
        const message = await response.text()
        throw new Error(message || 'Failed to create account.')
      }

      // On success, redirect to login page
      const messageReactive = messageDisplay.success(
        'Welcome to Solar Network! Your account has been created successfully.',
        { duration: 8000 },
      )
      setTimeout(() => {
        messageReactive.type = 'info'
        messageReactive.content = "Don't forget to check your email for activation instructions."
      }, 3000)
      router.push('/login')
    } catch (e: any) {
      error.value = e.message
    } finally {
      isLoading.value = false
    }
  })
}
</script>
