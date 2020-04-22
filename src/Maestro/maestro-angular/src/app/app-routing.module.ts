import { NgModule } from "@angular/core";
import { RouterModule, Routes } from "@angular/router";

import { MainComponent } from "./page/main/main.component";
import { BuildComponent } from "src/app/page/build/build.component";
import { ChannelResolverService } from './resolvers/channel-resolver.service';
import { BuildResolverService } from './resolvers/build-resolver.service';
import { toTitleCase } from 'src/helpers';
import { RepoNamePipe } from './pipes/repo-name.pipe';
import { ChannelComponent } from './page/channel/channel.component';

const routes: Routes = [
  {
    path: "",
    component: MainComponent,
    data: {
      title: "Index",
      name: "Index",
    },
  },
  {
    path: ":channelId/graph",
    component: ChannelComponent,
    data: {
      title(params: any) {
        return `Channel ${params.channelId}`;
      },
      name(params: any) {
        return `Channel ${params.channelId}`;
      }
    },
  },
  {
    path: ":channelId/:repository",
    redirectTo: ":channelId/:repository/latest/graph",
    pathMatch: "full",
  },
  {
    path: ":channelId/:repository/:buildId",
    redirectTo: ":channelId/:repository/:buildId/graph",
    pathMatch: "full",
  },
  {
    path: ":channelId/:repository/:buildId/:tabName",
    component: BuildComponent,
    data: {
      title(params: any, data: any) {
        const repo = RepoNamePipe.prototype.transform(params.repository);
        if (data.build === "latest") {
          return `Latest Build of ${repo} in ${data.channel.name} - ${toTitleCase(params.tabName)}`;
        }
        return `Build ${data.build.azureDevOpsBuildNumber} of ${repo} in ${data.channel.name} - ${toTitleCase(params.tabName)}`;
      },
      name(params: any) {
        return `Build - ${toTitleCase(params.tabName)}`;
      },
    },
    resolve: {
      channel: ChannelResolverService,
      build: BuildResolverService,
    },
  },
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule],
})
export class AppRoutingModule {}

