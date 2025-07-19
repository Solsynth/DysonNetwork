import '@fontsource-variable/nunito';

import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'

import Root from './root.vue'
import router from './router'

const app = createApp(Root)

app.use(createPinia())
app.use(router)

app.mount('#app')
