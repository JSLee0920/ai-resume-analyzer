import { Button } from "@/components/ui/button"

function App() {
  return (
    <main className="mx-auto flex min-h-svh max-w-5xl flex-col items-center justify-center gap-4 px-6 text-center">
      <h1 className="font-heading text-4xl font-medium tracking-tight text-foreground">
        Hello World!!
      </h1>
      <p className="text-muted-foreground">What is going on here!</p>
      <Button className="rounded-md">Get started</Button>
    </main>
  )
}

export default App
