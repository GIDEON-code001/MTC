import type { Metadata } from "next";
import type { ReactNode } from "react";
import { Providers } from "@/components/providers";
import "./globals.css";

export const metadata: Metadata = {
  title: {
    default: "BodaBoss Admin Portal",
    template: "%s · BodaBoss",
  },
  description: "Tenant-aware fleet operations administration for BodaBoss.",
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body><Providers>{children}</Providers></body>
    </html>
  );
}
