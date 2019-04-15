import { Component, Input, TemplateRef } from '@angular/core';
import { TreeNode } from '../definitions';

@Component({
  selector: 'tree-node-list',
  templateUrl: './tree-node-list.component.html',
  styleUrls: ['./tree-node-list.component.scss']
})
export class TreeNodeListComponent {
  @Input() public nodeTemplate?: TemplateRef<any>;
  @Input() public data?: TreeNode[];
}
