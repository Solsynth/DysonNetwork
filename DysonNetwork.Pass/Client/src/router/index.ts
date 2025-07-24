import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'index',
      component: () => import('../views/index.vue'),
    },
    {
      path: '/captcha',
      name: 'captcha',
      component: () => import('../views/captcha.vue'),
    },
    {
      path: '/spells/:word',
      name: 'spells',
      component: () => import('../views/spells.vue'),
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('../views/login.vue'),
    },
  ],
})

export default router
