<script setup lang="ts">
// Sign in as a web user. The users are set up by an Admin on the Users page
// (or from the FPVTrackside machine, which counts as Admin).
const { signIn, signInOpen } = useAuth()

const user = ref('')
const password = ref('')
const busy = ref(false)
const error = ref('')

watch(signInOpen, (o) => {
  if (o) {
    password.value = ''
    error.value = ''
  }
})

async function submit() {
  busy.value = true
  error.value = ''
  try {
    await signIn(user.value.trim(), password.value)
    signInOpen.value = false
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <UModal v-model:open="signInOpen" title="Sign in" description="Use the name and password an FPVTrackside Admin gave you.">
    <template #body>
      <form id="signin" class="grid gap-4" @submit.prevent="submit">
        <UFormField label="Name">
          <UInput v-model="user" autocomplete="username" autofocus class="w-full" />
        </UFormField>
        <UFormField label="Password">
          <UInput v-model="password" type="password" autocomplete="current-password" class="w-full" />
        </UFormField>
        <p v-if="error" class="text-sm text-error" role="alert">{{ error }}</p>
      </form>
    </template>
    <template #footer>
      <div class="flex w-full justify-end gap-2">
        <UButton color="neutral" variant="ghost" label="Cancel" @click="signInOpen = false" />
        <UButton type="submit" form="signin" icon="i-lucide-log-in" label="Sign in" :loading="busy" :disabled="!user.trim() || !password" />
      </div>
    </template>
  </UModal>
</template>
