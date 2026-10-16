<template>
  <UApp :toaster="{ position: 'bottom-right' }">
    <AppHeader />
    <SignInModal />
    <UMain>
      <NuxtPage />
    </UMain>
  </UApp>
</template>
