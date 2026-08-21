import AppLayout from "@/layouts/AppLayout";
import Dashboard from "@/pages/Dashboard";
import { createBrowserRouter } from "react-router-dom";

const router = createBrowserRouter([
  {
    path: "/",
    Component: AppLayout,
    children: [{ index: true, Component: Dashboard }],
  },
]);

export default router;
