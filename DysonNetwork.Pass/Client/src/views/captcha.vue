<template>
  <div class="h-full flex items-center justify-center">
    <n-card class="max-w-lg text-center" title="Captcha">
      <div class="flex justify-center my-4">
        <div v-if="provider === 'cloudflare'" class="cf-turnstile" :data-sitekey="apiKey" :data-callback="onSuccess"></div>
        <div v-else-if="provider === 'recaptcha'" class="g-recaptcha" :data-sitekey="apiKey" :data-callback="onSuccess"></div>
        <div v-else-if="provider === 'hcaptcha'" class="h-captcha" :data-sitekey="apiKey" :data-callback="onSuccess"></div>
        <div v-else class="alert alert-warning">
          <svg xmlns="http://www.w3.org/2000/svg" class="stroke-current shrink-0 h-6 w-6" fill="none" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
          </svg>
          <span>Captcha provider not configured correctly.</span>
        </div>
      </div>

      <div class="text-sm">
        <div class="font-semibold mb-1">Solar Network Anti-Robot</div>
        <div class="text-base-content/70">
          Powered by
          <template v-if="provider === 'cloudflare'">
            <a href="https://www.cloudflare.com/turnstile/" class="link link-hover">
              Cloudflare Turnstile
            </a>
          </template>
          <template v-else-if="provider === 'recaptcha'">
            <a href="https://www.google.com/recaptcha/" class="link link-hover">
              Google reCaptcha
            </a>
          </template>
          <template v-else>
            <span>Nothing</span>
          </template>
          <br/>
          Hosted by
          <a href="https://github.com/Solsynth/DysonNetwork" class="link link-hover">
            DysonNetwork.Sphere
          </a>
        </div>
      </div>
    </n-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { NCard } from 'naive-ui';

const route = useRoute();

// Get provider and API key from app data
// @ts-ignore
const { Provider: provider, ApiKey: apiKey } = window.__APP_DATA__ || {};

// Load the appropriate CAPTCHA script based on provider
const loadCaptchaScript = () => {
  if (!provider) return;

  const script = document.createElement('script');
  script.async = true;
  script.defer = true;

  switch (provider.toLowerCase()) {
    case 'recaptcha':
      script.src = 'https://www.recaptcha.net/recaptcha/api.js';
      break;
    case 'cloudflare':
      script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js';
      break;
    case 'hcaptcha':
      script.src = 'https://js.hcaptcha.com/1/api.js';
      break;
    default:
      return;
  }

  document.head.appendChild(script);
};

// Handle successful CAPTCHA verification
const onSuccess = (token: string) => {
  // Send token to parent window if in iframe
  if (window.parent !== window) {
    window.parent.postMessage(`captcha_tk=${token}`, '*');
  }

  // Handle redirect if redirect_uri is present
  const redirectUri = route.query.redirect_uri as string;
  if (redirectUri) {
    window.location.href = `${redirectUri}?captcha_tk=${encodeURIComponent(token)}`;
  }
};

// Load CAPTCHA script when component mounts
onMounted(() => {
  loadCaptchaScript();
});
</script>
