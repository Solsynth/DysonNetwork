import { createRouter, createWebHistory } from 'vue-router'
import { useUserStore } from '@/stores/user'

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
    {
      path: '/create-account',
      name: 'create-account',
      component: () => import('../views/create-account.vue'),
    },
    {
      path: '/accounts/:name',
      alias: ['/@:name'],
      name: 'accountProfilePage',
      component: () => import('../views/pfp/index.vue'),
    },
    {
      path: '/accounts/me',
      name: 'dashboard',
      meta: { requiresAuth: true },
      component: () => import('../layouts/dashboard.vue'),
      children: [
        {
          path: '',
          name: 'dashboardCurrent',
          component: () => import('../views/accounts/info.vue'),
          meta: { requiresAuth: true },
        },
        {
          path: 'security',
          name: 'dashboardSecurity',
          component: () => import('../views/accounts/security.vue'),
          meta: { requiresAuth: true },
        },
      ],
    },
    {
      path: '/auth/callback',
      name: 'authCallback',
      component: () => import('../views/callback.vue'),
    },
    {
      path: '/auth/authorize',
      name: 'authAuthorize',
      component: () => import('../views/authorize.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/:notFound(.*)',
      name: 'errorNotFound',
      component: () => import('../views/not-found.vue'),
    },
  ],
})

router.beforeEach(async (to, from, next) => {
  const userStore = useUserStore()

  // Initialize user state if not already initialized
  if (!userStore.user) {
    await userStore.fetchUser(false)
  }

  if (to.matched.some((record) => record.meta.requiresAuth) && !userStore.isAuthenticated) {
    next({ name: 'login', query: { redirect: to.fullPath } })
  } else {
    next()
  }
})

export default router
