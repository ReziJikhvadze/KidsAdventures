import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { AdminStateProvider } from "./components/AdminState";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Adventrya Admin UX",
  description:
    "Adventrya-ს შეკვეთების, წიგნის წარმოების, ბეჭდვისა და მიწოდების საოპერაციო სივრცე.",
  other: {
    "codex-preview": "development",
  },
  icons: {
    icon: "/favicon.svg",
    shortcut: "/favicon.svg",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ka">
      <body
        className={`${geistSans.variable} ${geistMono.variable} antialiased`}
      >
        <AdminStateProvider>{children}</AdminStateProvider>
      </body>
    </html>
  );
}
