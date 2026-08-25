import { Button } from "@/components/ui/button";

const Dashboard = () => {
  return (
    <div className="items-center justify-center gap-4 text-center">
      <h1 className="font-heading text-4xl font-medium tracking-tight text-foreground">
        Hello World!!
      </h1>
      <p className="text-muted-foreground">What is going on here!</p>
      <Button className="rounded-md">Get started</Button>
    </div>
  );
};

export default Dashboard;
