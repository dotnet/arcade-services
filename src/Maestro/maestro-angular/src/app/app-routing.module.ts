import { NgModule } from "@angular/core";
import { RouterModule, Routes } from "@angular/router";

import { MainComponent } from "./page/main/main.component";
import { BuildComponent } from "src/app/page/build/build.component";
import { RecentBuildComponent } from './page/recent-build/recent-build.component';

const routes: Routes = [
  { path: "", component: MainComponent },
  {
    path: ":channelId/:repository",
    component: RecentBuildComponent,
  },
  {
    path: ":channelId/:repository/:buildId",
    component: BuildComponent,
  },
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule],
})
export class AppRoutingModule {}

