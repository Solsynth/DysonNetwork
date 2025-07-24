<template>
  <div class="h-full flex items-center justify-center">
    <n-card class="max-w-lg text-center" title="Captcha Verification">
      <div class="mb-4 mt-2">
        <Captcha :provider="provider" :api-key="apiKey" @verified="onCaptchaVerified" />
      </div>
      <div class="text-sm">
        <div class="font-semibold mb-1">Solar Network Anti-Robot</div>
        <div class="text-base-content/70">
          Powered by
          <template v-if="provider === 'cloudflare'">
            <a href="https://www.cloudflare.com/turnstile/" class="link link-hover" target="_blank" rel="noopener noreferrer">
              Cloudflare Turnstile
            </a>
          </template>
          <template v-else-if="provider === 'recaptcha'">
            <a href="https://www.google.com/recaptcha/" class="link link-hover" target="_blank" rel="noopener noreferrer">
              Google reCaptcha
            </a>
          </template>
           <template v-else-if="provider === 'hcaptcha'">
            <a href="https://www.hcaptcha.com/" class="link link-hover" target="_blank" rel="noopener noreferrer">
              hCaptcha
            </a>
          </template>
          <template v-else>
            <span>Nothing</span>
          </template>
          <br/>
          Hosted by
          <a href="https://github.com/Solsynth/DysonNetwork" class="link link-hover" target="_blank" rel="noopener noreferrer">
            DysonNetwork.Sphere
          </a>
        </div>
      </div>
    </n-card>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useRoute } from 'vue-router';
import { NCard } from 'naive-ui';
import Captcha from '@/components/Captcha.vue';

const route = useRoute();

// Get provider and API key from app data
const provider = ref((window as any).__APP_DATA__?.Provider || '');
const apiKey = ref((window as any).__APP_DATA__?.ApiKey || '');

const onCaptchaVerified = (token: string) => {
  if (window.parent !== window) {
    window.parent.postMessage(`captcha_tk=${token}`, '*');
  }

  const redirectUri = route.query.redirect_uri as string;
  if (redirectUri) {
    window.location.href = `${redirectUri}?captcha_tk=${encodeURIComponent(token)}`;
  }
};
</script>